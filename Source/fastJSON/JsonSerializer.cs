using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Text;

namespace fastJSON
{
	internal sealed class JSONSerializer
	{
		private StringBuilder _output = new StringBuilder();
		//private StringBuilder _before = new StringBuilder();
		private int _before;
		private readonly int _MAX_DEPTH = 20;
		int _current_depth = 0;
		private Dictionary<string, int> _globalTypes = new Dictionary<string, int>();
		private Dictionary<object, int> _cirobj = new Dictionary<object, int>();
		private JSONParameters _params;
		private readonly bool _useEscapedUnicode = false;

		internal JSONSerializer(JSONParameters param)
		{
			_params = param;
			_useEscapedUnicode = _params.UseEscapedUnicode;
			_MAX_DEPTH = _params.SerializerMaxDepth;
		}

		internal string ConvertToJSON(object obj)
		{
			WriteValue(obj);

			if (_params.UsingGlobalTypes && _globalTypes != null && _globalTypes.Count > 0)
			{
				var sb = new StringBuilder();
				sb.Append("\"___types\":{");
				var pendingSeparator = false;
				foreach (var kv in _globalTypes)
				{
					if (pendingSeparator) sb.Append(',');
					pendingSeparator = true;
					sb.Append('\"');
					sb.Append(kv.Key);
					sb.Append("\":\"");
					sb.Append(kv.Value);
					sb.Append('\"');
				}
				sb.Append("},");
				_output.Insert(_before, sb.ToString());
			}
			return _output.ToString();
		}

		private void WriteValue(object obj)
		{
			if (obj == null || obj is DBNull)
				_output.Append("null");

			else if (obj is string || obj is char)
				WriteString(obj.ToString());

			else if (obj is Guid)
				WriteGuid((Guid)obj);

			else if (obj is bool)
				_output.Append(((bool)obj) ? "true" : "false"); // conform to standard

			else if (
				 obj is int || obj is long ||
				 obj is decimal ||
				 obj is byte || obj is short ||
				 obj is sbyte || obj is ushort ||
				 obj is uint || obj is ulong
			)
				_output.Append(((IConvertible)obj).ToString(NumberFormatInfo.InvariantInfo));

			else if (obj is double || obj is double)
			{
				var d = (double)obj;
				if (double.IsNaN(d))
					_output.Append("\"NaN\"");
				else if (double.IsInfinity(d))
				{
					_output.Append("\"");
					_output.Append(((IConvertible)obj).ToString(NumberFormatInfo.InvariantInfo));
					_output.Append("\"");
				}
				else
					_output.Append(((IConvertible)obj).ToString(NumberFormatInfo.InvariantInfo));
			}
			else if (obj is float || obj is float)
			{
				var d = (float)obj;
				if (float.IsNaN(d))
					_output.Append("\"NaN\"");
				else if (float.IsInfinity(d))
				{
					_output.Append("\"");
					_output.Append(((IConvertible)obj).ToString(NumberFormatInfo.InvariantInfo));
					_output.Append("\"");
				}
				else
					_output.Append(((IConvertible)obj).ToString(NumberFormatInfo.InvariantInfo));
			}

			else if (obj is DateTime)
				WriteDateTime((DateTime)obj);

			else if (obj is DateTimeOffset)
				WriteDateTimeOffset((DateTimeOffset)obj);

			else if (obj is TimeSpan)
				_output.Append(((TimeSpan)obj).Ticks);

			else if (_params.KVStyleStringDictionary == false && obj is IDictionary &&
				 obj.GetType().IsGenericType && Reflection.Instance.GetGenericArguments(obj.GetType())[0] == typeof(string))

				WriteStringDictionary((IDictionary)obj);
			else if (obj is IDictionary)
				WriteDictionary((IDictionary)obj);
			else if (obj is byte[])
				WriteBytes((byte[])obj);

			else if (obj is StringDictionary)
				WriteSD((StringDictionary)obj);

			else if (obj is NameValueCollection)
				WriteNV((NameValueCollection)obj);

			else if (obj is IEnumerable)
				WriteArray((IEnumerable)obj);

			else if (obj is Enum)
				WriteEnum((Enum)obj);

			else if (Reflection.Instance.IsTypeRegistered(obj.GetType()))
				WriteCustom(obj);

			else
				WriteObject(obj);
		}

		private void WriteDateTimeOffset(DateTimeOffset d)
		{
			var dt = _params.UseUTCDateTime ? d.UtcDateTime : d.DateTime;

			Write_date_value(dt);

			var ticks = dt.Ticks % TimeSpan.TicksPerSecond;
			_output.Append('.');
			_output.Append(ticks.ToString("0000000", NumberFormatInfo.InvariantInfo));

			if (_params.UseUTCDateTime)
				_output.Append('Z');
			else
			{
				if (d.Offset.Hours > 0)
					_output.Append("+");
				else
					_output.Append("-");
				_output.Append(d.Offset.Hours.ToString("00", NumberFormatInfo.InvariantInfo));
				_output.Append(":");
				_output.Append(d.Offset.Minutes.ToString("00", NumberFormatInfo.InvariantInfo));
			}

			_output.Append('\"');
		}

		private void WriteNV(NameValueCollection nameValueCollection)
		{
			_output.Append('{');

			var pendingSeparator = false;

			foreach (string key in nameValueCollection)
			{
				if (_params.SerializeNullValues == false && (nameValueCollection[key] == null))
				{
				}
				else
				{
					if (pendingSeparator) _output.Append(',');
					if (_params.SerializeToLowerCaseNames)
						WritePair(key.ToLowerInvariant(), nameValueCollection[key]);
					else
						WritePair(key, nameValueCollection[key]);
					pendingSeparator = true;
				}
			}
			_output.Append('}');
		}

		private void WriteSD(StringDictionary stringDictionary)
		{
			_output.Append('{');

			var pendingSeparator = false;

			foreach (DictionaryEntry entry in stringDictionary)
			{
				if (_params.SerializeNullValues == false && (entry.Value == null))
				{
				}
				else
				{
					if (pendingSeparator) _output.Append(',');

					var k = (string)entry.Key;
					if (_params.SerializeToLowerCaseNames)
						WritePair(k.ToLowerInvariant(), entry.Value);
					else
						WritePair(k, entry.Value);
					pendingSeparator = true;
				}
			}
			_output.Append('}');
		}

		private void WriteCustom(object obj)
		{
			Reflection.Serialize s;
			Reflection.Instance._customSerializer.TryGetValue(obj.GetType(), out s);
			WriteStringFast(s(obj));
		}

		private void WriteEnum(Enum e)
		{
			// FEATURE : optimize enum write
			if (_params.UseValuesOfEnums)
				WriteValue(Convert.ToInt32(e));
			else
				WriteStringFast(e.ToString());
		}

		private void WriteGuid(Guid g)
		{
			if (_params.UseFastGuid == false)
				WriteStringFast(g.ToString());
			else
				WriteBytes(g.ToByteArray());
		}

		private void WriteBytes(byte[] bytes)
		{
			WriteStringFast(Convert.ToBase64String(bytes, 0, bytes.Length, Base64FormattingOptions.None));
		}

		private void WriteDateTime(DateTime dateTime)
		{
			// datetime format standard : yyyy-MM-dd HH:mm:ss
			var dt = dateTime;
			if (_params.UseUTCDateTime)
				dt = dateTime.ToUniversalTime();

			Write_date_value(dt);

			if (_params.DateTimeMilliseconds)
			{
				_output.Append('.');
				_output.Append(dt.Millisecond.ToString("000", NumberFormatInfo.InvariantInfo));
			}

			if (_params.UseUTCDateTime)
				_output.Append('Z');

			_output.Append('\"');
		}

		private void Write_date_value(DateTime dt)
		{
			_output.Append('\"');
			_output.Append(dt.Year.ToString("0000", NumberFormatInfo.InvariantInfo));
			_output.Append('-');
			_output.Append(dt.Month.ToString("00", NumberFormatInfo.InvariantInfo));
			_output.Append('-');
			_output.Append(dt.Day.ToString("00", NumberFormatInfo.InvariantInfo));
			_output.Append('T'); // strict ISO date compliance 
			_output.Append(dt.Hour.ToString("00", NumberFormatInfo.InvariantInfo));
			_output.Append(':');
			_output.Append(dt.Minute.ToString("00", NumberFormatInfo.InvariantInfo));
			_output.Append(':');
			_output.Append(dt.Second.ToString("00", NumberFormatInfo.InvariantInfo));
		}

		bool _TypesWritten = false;
		private void WriteObject(object obj)
		{
			var i = 0;
			if (_cirobj.TryGetValue(obj, out i) == false)
				_cirobj.Add(obj, _cirobj.Count + 1);
			else
			{
				if (_current_depth > 0 && _params.InlineCircularReferences == false)
				{
					//_circular = true;
					_output.Append("{\"___i\":");
					_output.Append(i.ToString());
					_output.Append("}");
					return;
				}
			}
			if (_params.UsingGlobalTypes == false)
				_output.Append('{');
			else
			{
				if (_TypesWritten == false)
				{
					_output.Append('{');
					_before = _output.Length;
					//_output = new StringBuilder();
				}
				else
					_output.Append('{');
			}
			_TypesWritten = true;
			_current_depth++;
			if (_current_depth > _MAX_DEPTH)
				throw new Exception("Serializer encountered maximum depth of " + _MAX_DEPTH);


			var map = new Dictionary<string, string>();
			var t = obj.GetType();
			var append = false;
			if (_params.UseExtensions)
			{
				if (_params.UsingGlobalTypes == false)
					WritePairFast("___type", Reflection.Instance.GetTypeAssemblyName(t));
				else
				{
					var dt = 0;
					var ct = Reflection.Instance.GetTypeAssemblyName(t);
					if (_globalTypes.TryGetValue(ct, out dt) == false)
					{
						dt = _globalTypes.Count + 1;
						_globalTypes.Add(ct, dt);
					}
					WritePairFast("___type", dt.ToString());
				}
				append = true;
			}

			var g = Reflection.Instance.GetGetters(t, _params.ShowReadOnlyProperties, _params.IgnoreAttributes);
			var c = g.Length;
			for (var ii = 0; ii < c; ii++)
			{
				var p = g[ii];
				var o = p.Getter(obj);
				if (_params.SerializeNullValues == false && (o == null || o is DBNull))
				{
					//append = false;
				}
				else
				{
					if (append)
						_output.Append(',');
					if (p.memberName != null)
						WritePair(p.memberName, o);
					else if (_params.SerializeToLowerCaseNames)
						WritePair(p.lcName, o);
					else
						WritePair(p.Name, o);
					if (o != null && _params.UseExtensions)
					{
						var tt = o.GetType();
						if (tt == typeof(object))
							map.Add(p.Name, tt.ToString());
					}
					append = true;
				}
			}
			if (map.Count > 0 && _params.UseExtensions)
			{
				_output.Append(",\"___map\":");
				WriteStringDictionary(map);
			}
			_output.Append('}');
			_current_depth--;
		}

		private void WritePairFast(string name, string value)
		{
			WriteStringFast(name);

			_output.Append(':');

			WriteStringFast(value);
		}

		private void WritePair(string name, object value)
		{
			WriteString(name);

			_output.Append(':');

			WriteValue(value);
		}

		private void WriteArray(IEnumerable array)
		{
			_output.Append('[');

			var pendingSeperator = false;

			foreach (var obj in array)
			{
				if (pendingSeperator) _output.Append(',');

				WriteValue(obj);

				pendingSeperator = true;
			}
			_output.Append(']');
		}

		private void WriteStringDictionary(IDictionary dic)
		{
			_output.Append('{');

			var pendingSeparator = false;

			foreach (DictionaryEntry entry in dic)
			{
				if (_params.SerializeNullValues == false && (entry.Value == null))
				{
				}
				else
				{
					if (pendingSeparator) _output.Append(',');

					var k = (string)entry.Key;
					if (_params.SerializeToLowerCaseNames)
						WritePair(k.ToLowerInvariant(), entry.Value);
					else
						WritePair(k, entry.Value);
					pendingSeparator = true;
				}
			}
			_output.Append('}');
		}

		private void WriteStringDictionary(IEnumerable<KeyValuePair<string, object>> dic)
		{
			_output.Append('{');
			var pendingSeparator = false;
			foreach (var entry in dic)
			{
				if (_params.SerializeNullValues == false && (entry.Value == null))
				{
				}
				else
				{
					if (pendingSeparator) _output.Append(',');
					var k = entry.Key;

					if (_params.SerializeToLowerCaseNames)
						WritePair(k.ToLowerInvariant(), entry.Value);
					else
						WritePair(k, entry.Value);
					pendingSeparator = true;
				}
			}
			_output.Append('}');
		}

		private void WriteDictionary(IDictionary dic)
		{
			_output.Append('[');

			var pendingSeparator = false;

			foreach (DictionaryEntry entry in dic)
			{
				if (pendingSeparator) _output.Append(',');
				_output.Append('{');
				WritePair("k", entry.Key);
				_output.Append(",");
				WritePair("v", entry.Value);
				_output.Append('}');

				pendingSeparator = true;
			}
			_output.Append(']');
		}

		private void WriteStringFast(string s)
		{
			_output.Append('\"');
			_output.Append(s);
			_output.Append('\"');
		}

		private void WriteString(string s)
		{
			_output.Append('\"');

			var runIndex = -1;
			var l = s.Length;
			for (var index = 0; index < l; ++index)
			{
				var c = s[index];

				if (_useEscapedUnicode)
				{
					if (c >= ' ' && c < 128 && c != '\"' && c != '\\')
					{
						if (runIndex == -1)
							runIndex = index;

						continue;
					}
				}
				else
				{
					if (c != '\t' && c != '\n' && c != '\r' && c != '\"' && c != '\\' && c != '\0')// && c != ':' && c!=',')
					{
						if (runIndex == -1)
							runIndex = index;

						continue;
					}
				}

				if (runIndex != -1)
				{
					_output.Append(s, runIndex, index - runIndex);
					runIndex = -1;
				}

				switch (c)
				{
					case '\t': _output.Append("\\t"); break;
					case '\r': _output.Append("\\r"); break;
					case '\n': _output.Append("\\n"); break;
					case '"':
					case '\\': _output.Append('\\'); _output.Append(c); break;
					case '\0': _output.Append("\\u0000"); break;
					default:
						if (_useEscapedUnicode)
						{
							_output.Append("\\u");
							_output.Append(((int)c).ToString("X4", NumberFormatInfo.InvariantInfo));
						}
						else
							_output.Append(c);

						break;
				}
			}

			if (runIndex != -1)
				_output.Append(s, runIndex, s.Length - runIndex);

			_output.Append('\"');
		}
	}
}
