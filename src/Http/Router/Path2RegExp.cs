using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;

namespace IocpSharp.Http.Router
{
    public class Path2RegExpOptions
    {
        public bool strict = true;
        public bool start = true;
        public bool end = true;
        public string prefixes = "./";
        public string delimiter;
        public string endsWith;
    }

    /// <summary>
    /// 移植自JavaScript
    /// https://github.com/pillarjs/path-to-regexp
    /// </summary>
    public class Path2RegExp
    {
        private class LexerToken
        {
            public string type { get; set; }
            public int index { get; set; }
            public string value { get; set; }
        }

        private class Result
        {
            public string name { get; set; }
            public string prefix { get; set; }
            public string suffix { get; set; }
            public string pattern { get; set; }
            public string modifier { get; set; }
        }
        private static LexerToken[] lexer(string str)
        {
            var tokens = new List<LexerToken>();
            int i = 0;
            while (i < str.Length)
            {
                var chr = str[i];
                if (chr == '*' || chr == '+' || chr == '?')
                {
                    tokens.Add(new LexerToken { type = "MODIFIER", index = i, value = str.Substring(i++, 1) });
                    continue;
                }
                if (chr == '\\')
                {
                    tokens.Add(new LexerToken { type = "ESCAPED_CHAR", index = i++, value = str.Substring(i++, 1) });
                    continue;
                }
                if (chr == '{')
                {
                    tokens.Add(new LexerToken { type = "OPEN", index = i, value = str.Substring(i++, 1) });
                    continue;
                }
                if (chr == '}')
                {
                    tokens.Add(new LexerToken { type = "CLOSE", index = i, value = str.Substring(i++, 1) });
                    continue;
                }
                if (chr == ':')
                {
                    string name = "";
                    int j = i + 1;
                    while (j < str.Length)
                    {
                        char code = str[j];
                        if (
                        // `0-9`
                        (code >= 48 && code <= 57) ||
                            // `A-Z`
                            (code >= 65 && code <= 90) ||
                            // `a-z`
                            (code >= 97 && code <= 122) ||
                            // `_`
                            code == 95)
                        {
                            name += str[j++];
                            continue;
                        }
                        break;
                    }
                    if (string.IsNullOrEmpty(name))
                        throw new Exception($"Missing parameter name at { i }");

                    tokens.Add(new LexerToken { type = "NAME", index = i, value = name });
                    i = j;
                    continue;
                }
                if (chr == '(')
                {
                    int count = 1;
                    string pattern = "";
                    int j = i + 1;
                    if (str[j] == '?')
                    {
                        throw new Exception($"Pattern cannot start with \" ? \" at { j }");
                    }
                    while (j < str.Length)
                    {
                        if (str[j] == '\\')
                        {
                            pattern += str.Substring(j, 2);
                            j += 2;
                            continue;
                        }
                        if (str[j] == ')')
                        {
                            count--;
                            if (count == 0)
                            {
                                j++;
                                break;
                            }
                        }
                        else if (str[j] == '(')
                        {
                            count++;
                            if (str[j + 1] != '?')
                            {
                                throw new Exception($"Capturing groups are not allowed at ${ j }");
                            }
                        }
                        pattern += str.Substring(j++, 1);
                    }
                    if (count > 0)
                        throw new Exception($"Unbalanced pattern at { i }");
                    if (string.IsNullOrEmpty(pattern))
                        throw new Exception($"Missing pattern at { i }");
                    tokens.Add(new LexerToken { type = "PATTERN", index = i, value = pattern });
                    i = j;
                    continue;
                }
                tokens.Add(new LexerToken { type = "CHAR", index = i, value = str.Substring(i++, 1) });
            }
            tokens.Add(new LexerToken { type = "END", index = i, value = "" });
            return tokens.ToArray();
        }

        private static string tryConsume(LexerToken[] tokens, string type, ref int i)
        {
            if (i < tokens.Length && tokens[i].type == type)
                return tokens[i++].value;
            return null;
        }

        private static string mustConsume(LexerToken[] tokens, string type, ref int i) {

            string value = tryConsume(tokens, type, ref i);
            if (value != null)
                return value;
            string nextType = tokens[i].type;
            int index = tokens[i].index;
            throw new Exception($"Unexpected { nextType } at { index}, expected { type}");
        }

        private static string consumeText(LexerToken[] tokens, ref int i)
        {
            string result_ = "";
            string value;
            while (!string.IsNullOrEmpty(value = tryConsume(tokens, "CHAR", ref i) ?? tryConsume(tokens, "ESCAPED_CHAR", ref i)))
            {
                result_ += value;
            }
            return result_;
        }
        /**
         * Parse a string for the raw tokens.
         */
        private static object[] parse(string str, Path2RegExpOptions options)
        {
            string prefixes = options.prefixes;
            string delimiter = options.delimiter;
            var tokens = lexer(str);
            var defaultPattern = $"[^{escapeString(delimiter ?? "/#?")}]+?";
            var result = new List<object>();
            int key = 1;
            int i = 0;
            string path = "";
            while (i < tokens.Length)
            {
                string char_ = tryConsume(tokens, "CHAR", ref i);
                string name = tryConsume(tokens, "NAME", ref i);
                string pattern = tryConsume(tokens, "PATTERN", ref i);
                if (!string.IsNullOrEmpty(name) || !string.IsNullOrEmpty(pattern))
                {
                    string prefix = char_ ?? "";
                    if (prefixes.IndexOf(prefix) == -1)
                    {
                        path += prefix;
                        prefix = "";
                    }
                    if (!string.IsNullOrEmpty(path))
                    {
                        result.Add(path);
                        path = "";
                    }
                    result.Add(new Result
                    {
                        name = name ?? (key++).ToString(),
                        prefix = prefix,
                        suffix = "",
                        pattern = pattern ?? defaultPattern,
                        modifier = tryConsume(tokens, "MODIFIER", ref i) ?? ""
                    });
                    continue;
                }
                string value = char_ ?? tryConsume(tokens, "ESCAPED_CHAR", ref i);
                if (!string.IsNullOrEmpty(value))
                {
                    path += value;
                    continue;
                }
                if (!string.IsNullOrEmpty(path))
                {
                    result.Add(path);
                    path = "";
                }
                string open = tryConsume(tokens, "OPEN", ref i);
                if (!string.IsNullOrEmpty(open))
                {
                    string prefix = consumeText(tokens, ref i);
                    string name_ = tryConsume(tokens, "NAME", ref i) ?? "";
                    string pattern_ = tryConsume(tokens, "PATTERN", ref i) ?? "";
                    string suffix = consumeText(tokens, ref i);
                    mustConsume(tokens, "CLOSE", ref i);
                    result.Add(new Result
                    {
                        name = name_ ?? (!string.IsNullOrEmpty(pattern_) ? (key++).ToString() : ""),
                        pattern = (!string.IsNullOrEmpty(name_) && string.IsNullOrEmpty(pattern_)) ? defaultPattern : pattern_,
                        prefix = prefix,
                        suffix = suffix,
                        modifier = tryConsume(tokens, "MODIFIER", ref i) ?? ""
                    });
                    continue;
                }
                mustConsume(tokens, "END", ref i);
            }
            return result.ToArray();
        }

        /**
         * Escape a regular expression string.
         */
        private static string escapeString(string str)
        {
            return Regex.Replace(str, @"([.+*?=^!:${}()[\]|/\\])", "\\$1");
        }
        /**
         * Create a path regexp from string input.
         */
        public static string compile(string path)
        {
            return compile(path, new Path2RegExpOptions());
        }
        /**
         * Create a path regexp from string input.
         */
        public static string compile(string path, Path2RegExpOptions options)
        {
            return tokensToRegexp(parse(path, options), options);
        }
        private static string encode(string x)
        {
            return x;
        }
        /**
         * Expose a private void for taking tokens and returning a RegExp.
         */
        private static string tokensToRegexp(object[] tokens, Path2RegExpOptions options = null)
        {

            bool strict = options.strict;
            bool start = options.start;
            bool end = options.end;
            string endsWith = $"[{ escapeString(options.endsWith ?? "")}]|$";
            string delimiter = $"[{ escapeString(options.delimiter ?? "/#?")}]";
            string route = start ? "^" : "";
            // Iterate over the tokens and create our regexp string.
            foreach (object token in tokens)
            {
                string stringToken = token as string;
                if (stringToken != null)
                {
                    route += escapeString(encode(stringToken));
                }
                else
                {
                    Result objectToken = token as Result;
                    string prefix = escapeString(encode(objectToken.prefix));
                    string suffix = escapeString(encode(objectToken.suffix));
                    string namedPattern = !string.IsNullOrEmpty(objectToken.name) ? $"?<{objectToken.name}>" : "";
                    if (!string.IsNullOrEmpty(objectToken.pattern))
                    {
                        if (!string.IsNullOrEmpty(prefix) || !string.IsNullOrEmpty(suffix))
                        {
                            if (objectToken.modifier == "+" || objectToken.modifier == "*")
                            {
                                string mod = objectToken.modifier == "*" ? "?" : "";
                                route += $"(?:{prefix}({namedPattern}(?:{objectToken.pattern})(?:{suffix}{prefix}(?:{objectToken.pattern}))*){suffix}){mod}";
                            }
                            else
                            {
                                route += $"(?:{prefix}({namedPattern}{objectToken.pattern}){suffix}){objectToken.modifier}";
                            }
                        }
                        else
                        {
                            route += $"({namedPattern}{objectToken.pattern}){objectToken.modifier}";
                        }
                    }
                    else
                    {
                        route += $"(?:{prefix}{suffix}){objectToken.modifier}";
                    }
                }
            }
            if (end)
            {
                if (!strict)
                    route += $"{delimiter}?";
                route += string.IsNullOrEmpty(options.endsWith) ? "$" : $"(?={endsWith})";
            }
            else
            {
                object endToken = tokens[tokens.Length - 1];
                bool isEndDelimited;

                string stringToken = endToken as string;
                if (stringToken != null)
                {
                    isEndDelimited = delimiter.IndexOf(stringToken[stringToken.Length - 1]) > -1;
                }
                else
                {
                    isEndDelimited = endToken == null;
                }

                if (!strict)
                {
                    route += $"(?:{delimiter}(?={endsWith}))?";
                }
                if (!isEndDelimited)
                {
                    route += $"(?={delimiter}|{endsWith})";
                }
            }
            return route;
        }
    }
}
