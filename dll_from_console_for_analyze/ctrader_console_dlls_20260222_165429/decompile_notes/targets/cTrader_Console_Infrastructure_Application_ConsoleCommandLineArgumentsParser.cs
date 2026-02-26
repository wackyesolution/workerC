using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Core.Autofac.Extension;
using Core.Framework.Extension.Application.Instrumentation.Log;

namespace cTrader.Console.Infrastructure.Application;

[Export(InstanceKind.Single, new Type[] { typeof(IConsoleCommandLineArgumentsParser) })]
public class ConsoleCommandLineArgumentsParser : IConsoleCommandLineArgumentsParser
{
	private static readonly ILogger Logger = LoggerFactory.GetClassLogger("ConsoleCommandLineArgumentsParser");

	private readonly Regex _longKeyRegex;

	private readonly Regex _shortKeyRegex;

	public ConsoleCommandLineArgumentsParser()
	{
		_shortKeyRegex = new Regex("(?<=^-)(?s)([^-].*$)");
		_longKeyRegex = new Regex("(?<=^--)(?s)(.*$)");
	}

	public IDictionary<string, string?> Parse(IEnumerable<string> arguments)
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>();
		List<string> list = new List<string>(arguments);
		while (list.Any())
		{
			string text = list.First();
			string key = GetKey(text);
			list.Remove(text);
			if (string.IsNullOrEmpty(key))
			{
				Logger.Warn("Wrong key name " + text);
				continue;
			}
			string text2 = list.FirstOrDefault();
			if (text2 == null || _shortKeyRegex.IsMatch(text2) || _longKeyRegex.IsMatch(text2))
			{
				dictionary.Add(key, null);
				continue;
			}
			dictionary.Add(key, text2.Trim('"'));
			list.Remove(text2);
		}
		return dictionary;
	}

	private string GetKey(string word)
	{
		Match match = _shortKeyRegex.Match(word);
		if (match.Success)
		{
			return match.Groups[0].Value;
		}
		Match match2 = _longKeyRegex.Match(word);
		if (match2.Success)
		{
			return match2.Groups[0].Value;
		}
		return string.Empty;
	}
}
