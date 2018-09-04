using System.Text;

namespace fastJSON
{
	internal static class Formatter
	{
		private static string _indent = "   ";

		public static void AppendIndent(StringBuilder sb, int count)
		{
			for (; count > 0; --count) sb.Append(_indent);
		}

		public static string PrettyPrint(string input)
		{
			return PrettyPrint(input, "   ");
		}

		public static string PrettyPrint(string input, string spaces)
		{
			_indent = spaces;
			var output = new StringBuilder();
			var depth = 0;
			var len = input.Length;
			var chars = input.ToCharArray();
			for (var i = 0; i < len; ++i)
			{
				var ch = chars[i];

				if (ch == '\"') // found string span
				{
					var str = true;
					while (str)
					{
						output.Append(ch);
						ch = chars[++i];
						if (ch == '\\')
						{
							output.Append(ch);
							ch = chars[++i];
						}
						else if (ch == '\"')
							str = false;
					}
				}

				switch (ch)
				{
					case '{':
					case '[':
						output.Append(ch);
						output.AppendLine();
						AppendIndent(output, ++depth);
						break;
					case '}':
					case ']':
						output.AppendLine();
						AppendIndent(output, --depth);
						output.Append(ch);
						break;
					case ',':
						output.Append(ch);
						output.AppendLine();
						AppendIndent(output, depth);
						break;
					case ':':
						output.Append(" : ");
						break;
					default:
						if (!char.IsWhiteSpace(ch))
							output.Append(ch);
						break;
				}
			}

			return output.ToString();
		}
	}
}