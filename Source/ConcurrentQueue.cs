using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Verse;

namespace ZombieLand
{
	public class ConcurrentQueue<T>
	{
		readonly List<T> queue = new List<T>();
		readonly int maxSize;
		readonly bool returnNullOnEmpty;

		public ConcurrentQueue(bool returnNullOnEmpty = false, int maxSize = int.MaxValue)
		{
			this.maxSize = maxSize;
			this.returnNullOnEmpty = returnNullOnEmpty;
		}

		public void Enqueue(T item, Func<T, bool> overwritePredicate = null)
		{
			lock (queue)
			{
				while (queue.Count >= maxSize)
					Monitor.Wait(queue);

				if (overwritePredicate == null)
					queue.Add(item);
				else
				{
					var idx = queue.FirstIndexOf(overwritePredicate);
					if (idx >= 0 && idx < queue.Count)
						queue[idx] = item;
					else
						queue.Add(item);
				}

				if (queue.Count == 1)
					Monitor.PulseAll(queue);
			}
		}

		public T Dequeue()
		{
			lock (queue)
			{
				while (queue.Count == 0)
				{
					if (returnNullOnEmpty)
						return default;

					Monitor.Wait(queue);
				}

				var item = queue[0];
				queue.RemoveAt(0);

				if (queue.Count == maxSize - 1)
					Monitor.PulseAll(queue);

				return item;
			}
		}

		public int Count()
		{
			lock (queue)
			{
				return queue.Count();
			}
		}

		public int Count(Func<T, bool> predicate)
		{
			lock (queue)
			{
				return queue.Select(predicate).Count();
			}
		}
	}
}