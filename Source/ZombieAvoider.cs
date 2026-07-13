using RimWorld;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Unity.Collections;
using Verse;

namespace ZombieLand
{
	public class ZombieCostSpecs
	{
		public IntVec3 position;
		public float radius;
		public float maxCosts;
	}

	sealed class ZombieAvoidGridNativeSnapshot
	{
		static long nextGenerationId;
		readonly object sync = new();
		NativeArray<ushort> offsets;
		int activeOwners;
		int requestReferences;
		bool disposed;

		public readonly long generationId;

		public ZombieAvoidGridNativeSnapshot(int[] costs)
		{
			generationId = Interlocked.Increment(ref nextGenerationId);
			offsets = new NativeArray<ushort>(costs.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
			for (var i = 0; i < costs.Length; i++)
			{
				var cost = costs[i];
				offsets[i] = cost <= 0 ? (ushort)0 : cost >= ushort.MaxValue ? ushort.MaxValue : (ushort)cost;
			}
			AvoidGrid.RecordNativeSnapshotAllocation(costs.Length);
		}

		public bool IsDisposed
		{
			get
			{
				lock (sync)
					return disposed;
			}
		}

		public NativeArray<ushort> Offsets
		{
			get
			{
				lock (sync)
					return offsets;
			}
		}

		public void Activate()
		{
			lock (sync)
			{
				if (disposed)
					throw new ObjectDisposedException(nameof(ZombieAvoidGridNativeSnapshot));
				activeOwners++;
			}
		}

		public void Deactivate()
		{
			lock (sync)
			{
				if (activeOwners > 0)
					activeOwners--;
				DisposeIfUnused();
			}
		}

		public void AcquireRequest()
		{
			lock (sync)
			{
				if (disposed)
					throw new ObjectDisposedException(nameof(ZombieAvoidGridNativeSnapshot));
				requestReferences++;
			}
		}

		public void ReleaseRequest()
		{
			lock (sync)
			{
				if (requestReferences > 0)
					requestReferences--;
				DisposeIfUnused();
			}
		}

		void DisposeIfUnused()
		{
			if (disposed || activeOwners != 0 || requestReferences != 0)
				return;
			if (offsets.IsCreated)
				offsets.Dispose();
			disposed = true;
		}
	}

	public class AvoidGrid
	{
		readonly int[] costs;
		int[] newCosts;
		readonly int mapSize;
		readonly object nativeSnapshotLock = new();
		ZombieAvoidGridNativeSnapshot nativeSnapshot;
		bool hasCosts;
		static long nativeSnapshotAllocationCount;
		static long nativeSnapshotCopiedCellCount;
		static long pathCustomizerCreationCount;
		public long requestId;
		public FloodFiller filler;

		public AvoidGrid(Map map)
		{
			mapSize = map.Size.x * map.Size.z;
			costs = new int[mapSize];
			filler = new FloodFiller(map);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int[] GetCosts()
		{
			return costs;
		}

		public bool HasCosts => hasCosts;

		public static long NativeSnapshotAllocationCount => Interlocked.Read(ref nativeSnapshotAllocationCount);

		public static long NativeSnapshotCopiedCellCount => Interlocked.Read(ref nativeSnapshotCopiedCellCount);

		public static long PathCustomizerCreationCount => Interlocked.Read(ref pathCustomizerCreationCount);

		internal static void RecordNativeSnapshotAllocation(int copiedCells)
		{
			_ = Interlocked.Increment(ref nativeSnapshotAllocationCount);
			_ = Interlocked.Add(ref nativeSnapshotCopiedCellCount, copiedCells);
		}

		internal static void RecordPathCustomizerCreation()
		{
			_ = Interlocked.Increment(ref pathCustomizerCreationCount);
		}

		internal ZombieAvoidGridNativeSnapshot GetOrCreateNativeSnapshot()
		{
			lock (nativeSnapshotLock)
			{
				if (nativeSnapshot == null || nativeSnapshot.IsDisposed)
					nativeSnapshot = new ZombieAvoidGridNativeSnapshot(costs);
				return nativeSnapshot;
			}
		}

		public bool InAvoidDanger(Pawn pawn)
		{
			return GetCosts()[pawn.Position.x + pawn.Position.z * pawn.Map.Size.x] > 0;
		}

		public bool ShouldAvoid(Map map, IntVec3 position)
		{
			return GetCosts()[position.x + position.z * map.Size.x] > 0;
		}

		public int[] GetNewCosts()
		{
			newCosts ??= new int[mapSize];
			return newCosts;
		}

		public void ClearNewCosts()
		{
			if (newCosts != null)
				Array.Clear(newCosts, 0, mapSize);
		}

		public void FinalizeCosts()
		{
			if (newCosts == null)
				return;
			hasCosts = false;
			for (var i = 0; i < mapSize; i++)
			{
				var cost = newCosts[i];
				costs[i] = cost;
				if (cost > 0)
					hasCosts = true;
			}
			newCosts = null;
			// A finalized grid is a new immutable generation. If a diagnostic or future
			// caller reuses the AvoidGrid instance, the registry will retire the previous
			// native snapshot after its outstanding path-job leases complete.
			lock (nativeSnapshotLock)
				nativeSnapshot = null;
		}
	}

	class AvoidRequest
	{
		public Map map;
		public List<ZombieCostSpecs> specs;
		public long requestId;
	}

	[StaticConstructorOnStartup]
	public class ZombieAvoider
	{
		readonly ConcurrentQueue<AvoidRequest> requestQueue;
		readonly Dictionary<Map, ConcurrentQueue<AvoidGrid>> resultQueues;
		readonly Dictionary<Map, long> completedRequestIds;
		readonly object workerLock;
		Thread workerThread;
		Thread rescueWorkerThread;
		long lastWarningTicks;
		long lastRescueTicks;
		long nextRequestId;
		public volatile bool running;

		ConcurrentQueue<AvoidGrid> QueueForMap(Map map)
		{
			lock (requestQueue)
			{
				if (resultQueues.TryGetValue(map, out var queue) == false)
				{
					queue = new ConcurrentQueue<AvoidGrid>(true);
					resultQueues.Add(map, queue);
				}
				return queue;
			}
		}

		public ZombieAvoider()
		{
			requestQueue = new ConcurrentQueue<AvoidRequest>();
			resultQueues = new Dictionary<Map, ConcurrentQueue<AvoidGrid>>();
			completedRequestIds = new Dictionary<Map, long>();
			workerLock = new object();

			running = true;
			EnsureWorkerRunning();
		}

		public long UpdateZombiePositions(Map map, List<ZombieCostSpecs> specs)
		{
			if (map == null)
				return 0;
			var copiedSpecs = CopySpecs(specs);
			lock (workerLock)
			{
				if (running == false)
					return 0;
				EnsureWorkerRunning();
				var request = new AvoidRequest() { map = map, specs = copiedSpecs, requestId = Interlocked.Increment(ref nextRequestId) };
				requestQueue.Enqueue(request, req => req.map == map);
				return request.requestId;
			}
		}

		public AvoidGrid UpdateZombiePositionsImmediately(Map map, List<ZombieCostSpecs> specs)
		{
			if (map == null)
				return null;
			var copiedSpecs = CopySpecs(specs);
			lock (workerLock)
			{
				if (running == false)
					return null;
				var request = new AvoidRequest() { map = map, specs = copiedSpecs, requestId = Interlocked.Increment(ref nextRequestId) };
				var result = ProcessRequest(request, out var error);
				if (error != null)
					WarnProcessingFailure("synchronous avoid-grid rebuild", error, request);
				return result;
			}
		}

		public AvoidGrid GetCostsGrid(Map map)
		{
			if (map == null)
				return null;
			lock (workerLock)
			{
				if (running == false)
					return null;
				var queue = QueueForMap(map);
				return queue.DequeueLatest();
			}
		}

		static TraverseParms traverseParms = TraverseParms.For(TraverseMode.PassDoors, Danger.None, false, true, false);
		static bool CanProcessMap(Map map)
		{
			return map != null
				&& map.Size.x > 0
				&& map.Size.z > 0
				&& map.pathing != null
				&& map.edificeGrid != null
				&& map.thingGrid != null;
		}

		static bool ValidSpec(Map map, ZombieCostSpecs spec)
		{
			return spec != null
				&& spec.position.IsValid
				&& spec.position.InBounds(map)
				&& spec.radius > 0f
				&& spec.maxCosts > 0f
				&& float.IsNaN(spec.radius) == false
				&& float.IsInfinity(spec.radius) == false
				&& float.IsNaN(spec.maxCosts) == false
				&& float.IsInfinity(spec.maxCosts) == false;
		}

		static List<ZombieCostSpecs> CopySpecs(List<ZombieCostSpecs> specs)
		{
			if (specs == null || specs.Count == 0)
				return new List<ZombieCostSpecs>();

			var result = new List<ZombieCostSpecs>(specs.Count);
			foreach (var spec in specs)
			{
				if (spec == null)
				{
					result.Add(null);
					continue;
				}

				result.Add(new ZombieCostSpecs()
				{
					position = spec.position,
					radius = spec.radius,
					maxCosts = spec.maxCosts
				});
			}
			return result;
		}

		static void GenerateCells(Map map, List<ZombieCostSpecs> specs, int[] costCells, FloodFiller filler)
		{
			var mapSizeX = map.Size.x;
			var pathGrid = map.pathing.For(traverseParms).pathGrid;
			var cardinals = GenAdj.CardinalDirections;

			foreach (var spec in specs)
			{
				if (ValidSpec(map, spec) == false)
					continue;

				var loc = spec.position;
				var costBase = spec.maxCosts;
				var radiusSquared = spec.radius * spec.radius;

				var floodedCells = new Dictionary<IntVec3, int>();
				filler.FloodFill(loc,
					cell =>
						(loc - cell).LengthHorizontalSquared <= radiusSquared
						&& pathGrid.Walkable(cell)
						&& (cell.GetEdifice(map) is Building_Door) == false,
					cell =>
					{
						var f = 1f - (loc - cell).LengthHorizontalSquared / radiusSquared;
						var cost = (int)(costBase * f);
						var idx = cell.x + cell.z * mapSizeX;
						costCells[idx] = Math.Max(costCells[idx], cost);
						floodedCells[cell] = costCells[idx];
					});

				foreach (var cell in floodedCells.Keys)
					for (var i = 0; i <= 3; i++)
					{
						var pos = cell + cardinals[i];
						if (floodedCells.ContainsKey(pos) == false
							&& (loc - cell).LengthHorizontalSquared <= radiusSquared
							&& pos.InBounds(map) && pos.GetEdifice(map) is Building_Door)
						{
							costCells[pos.x + pos.z * mapSizeX] = floodedCells[cell];
						}
					}
			}
		}

		AvoidGrid ProcessRequest(AvoidRequest request, out Exception error)
		{
			error = null;
			var avoidGrid = new AvoidGrid(request.map);
			avoidGrid.ClearNewCosts();
			try
			{
				if (CanProcessMap(request.map))
					GenerateCells(request.map, request.specs ?? new List<ZombieCostSpecs>(), avoidGrid.GetNewCosts(), avoidGrid.filler);
			}
			catch (Exception e)
			{
				error = e;
				avoidGrid.ClearNewCosts();
			}
			avoidGrid.requestId = request.requestId;
			avoidGrid.FinalizeCosts();
			return avoidGrid;
		}

		bool ProcessOneRequest(bool waitForRequest)
		{
			AvoidRequest request = null;
			try
			{
				if (waitForRequest)
					request = requestQueue.Dequeue();
				else if (requestQueue.TryDequeue(out request) == false)
					return false;

				if (request?.map == null)
					return true;

				var result = ProcessRequest(request, out var error);
				if (error != null)
					WarnProcessingFailure("async avoid-grid rebuild", error, request);

				var queue = QueueForMap(request.map);
				queue.ReplaceIf(result, (existing, incoming) => incoming.requestId >= existing.requestId);
				MarkRequestCompleted(request);
			}
			catch (ThreadAbortException)
			{
				return false;
			}
			catch (Exception e)
			{
				WarnProcessingFailure("worker loop", e, request);
				Thread.Sleep(500);
			}
			return true;
		}

		void WorkerLoop(bool waitForRequest)
		{
			while (running)
			{
				if (ProcessOneRequest(waitForRequest) == false)
					return;
				if (waitForRequest == false)
					return;
			}
		}

		void MarkRequestCompleted(AvoidRequest request)
		{
			if (request?.map == null)
				return;

			lock (workerLock)
			{
				if (completedRequestIds.TryGetValue(request.map, out var completedRequestId) == false || request.requestId > completedRequestId)
					completedRequestIds[request.map] = request.requestId;
			}
		}

		bool EnsureWorkerRunning()
		{
			if (running == false)
				return false;

			lock (workerLock)
			{
				if (running == false)
					return false;
				if (workerThread?.IsAlive == true)
					return false;

				workerThread = new Thread(() => WorkerLoop(true))
				{
					Priority = ThreadPriority.Lowest,
					IsBackground = true
				};
				workerThread.Start();
				return true;
			}
		}

		public void RecoverWorkerIfStale(Map map, long requestId)
		{
			if (map == null || requestId <= 0 || running == false)
				return;
			if (EnsureWorkerRunning())
				return;

			lock (workerLock)
			{
				if (running == false)
					return;
				if (completedRequestIds.TryGetValue(map, out var completedRequestId) && completedRequestId >= requestId)
					return;
				if (rescueWorkerThread?.IsAlive == true)
					return;

				var now = DateTime.UtcNow.Ticks;
				if (now - lastRescueTicks < TimeSpan.TicksPerSecond * 10)
					return;

				lastRescueTicks = now;
				rescueWorkerThread = new Thread(() => WorkerLoop(false))
				{
					Priority = ThreadPriority.Lowest,
					IsBackground = true
				};
				rescueWorkerThread.Start();
			}
		}

		public void StopAndDrain()
		{
			Thread primary;
			Thread rescue;
			lock (workerLock)
			{
				running = false;
				primary = workerThread;
				rescue = rescueWorkerThread;
			}

			// Wake the blocking worker. Two sentinels cover the primary and rescue
			// workers racing to consume the queue while shutdown begins.
			requestQueue.Enqueue(new AvoidRequest());
			requestQueue.Enqueue(new AvoidRequest());

			JoinWorker(primary);
			JoinWorker(rescue);

			while (requestQueue.TryDequeue(out _))
			{
			}
			lock (requestQueue)
				resultQueues.Clear();
			lock (workerLock)
			{
				completedRequestIds.Clear();
				workerThread = null;
				rescueWorkerThread = null;
			}
		}

		static void JoinWorker(Thread thread)
		{
			if (thread == null || thread == Thread.CurrentThread)
				return;
			thread.Join();
		}

		void WarnProcessingFailure(string context, Exception error, AvoidRequest request)
		{
			var now = DateTime.UtcNow.Ticks;
			lock (workerLock)
			{
				if (now - lastWarningTicks < TimeSpan.TicksPerSecond * 10)
					return;
				lastWarningTicks = now;
			}

			var mapId = request?.map == null ? "none" : request.map.uniqueID.ToString();
			var specCount = request?.specs?.Count ?? 0;
			Log.Warning($"ZombieAvoider recovered from {context} for map {mapId}, specs {specCount}: {error}");
		}
	}
}
