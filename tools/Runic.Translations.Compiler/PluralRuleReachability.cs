using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Runic.Translations.Compiler;

// The plural categories an integer operand (v = w = f = t = e = 0) can reach under a generated CLDR rule.
// It evaluates the generated ESM rule source, the same data both runtimes select with, over the integers
// CLDR's samples are drawn from: 0..1000, powers of ten and multiples of a million (the fr/es/it `many`).
// Only the expression subset eng/generate-cldr.mjs emits is accepted; anything else returns null so the
// caller keeps the full category list instead of guessing.
internal static class PluralRuleReachability
{
    private static readonly Regex Token = new(@"\s*(?:(?<word>if|return)\b|(?<operand>o\.[a-z])|(?<number>\d+)n|""(?<string>[a-z]+)""|(?<op>===|!==|>=|<=|&&|\|\||[><%!(){};]))",
        RegexOptions.CultureInvariant);

    internal static IReadOnlySet<string>? IntegerCategories(string body)
    {
        List<string>? tokens = Tokenize(body);
        if (tokens is null) return null;
        var reached = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (BigInteger sample in Samples())
                reached.Add(Evaluate(tokens, sample));
        }
        catch (FormatException)
        {
            return null;
        }
        return reached;
    }

    private static IEnumerable<BigInteger> Samples()
    {
        for (int value = 0; value <= 1000; value++) yield return value;
        BigInteger power = 1;
        for (int exponent = 1; exponent <= 18; exponent++) yield return power *= 10;
        for (int millions = 1; millions <= 20; millions++) yield return millions * new BigInteger(1_000_000);
    }

    private static List<string>? Tokenize(string body)
    {
        var tokens = new List<string>();
        int position = 0;
        while (position < body.Length)
        {
            if (char.IsWhiteSpace(body[position])) { position++; continue; }
            Match match = Token.Match(body, position);
            if (!match.Success || match.Index != position) return null;
            tokens.Add(match.Groups["number"].Success ? "#" + match.Groups["number"].Value : match.Groups["string"].Success ? "\"" + match.Groups["string"].Value : match.Value.Trim());
            position += match.Length;
        }
        return tokens;
    }

    // body := (if ( expr ) return "c" ;)* return "c" ;
    private static string Evaluate(List<string> tokens, BigInteger n)
    {
        var parser = new Parser(tokens, n);
        while (true)
        {
            if (parser.Accept("return")) return parser.Category();
            parser.Expect("if");
            parser.Expect("(");
            bool condition = Parser.Boolean(parser.Or());
            parser.Expect(")");
            parser.Expect("return");
            string category = parser.Category();
            if (condition) return category;
        }
    }

    private sealed class Parser(List<string> tokens, BigInteger n)
    {
        private int _next;

        internal bool Accept(string token)
        {
            if (_next < tokens.Count && tokens[_next] == token) { _next++; return true; }
            return false;
        }

        internal void Expect(string token)
        {
            if (!Accept(token)) throw new FormatException("Unexpected plural rule token.");
        }

        internal string Category()
        {
            if (_next >= tokens.Count || !tokens[_next].StartsWith('"')) throw new FormatException("Expected a plural category.");
            string category = tokens[_next++][1..];
            Expect(";");
            return category;
        }

        internal static bool Boolean(object value) => value as bool? ?? throw new FormatException("Expected a condition.");
        private static BigInteger Number(object value) => value as BigInteger? ?? throw new FormatException("Expected a number.");

        internal object Or()
        {
            object left = And();
            while (Accept("||")) { bool right = Boolean(And()); left = Boolean(left) || right; }
            return left;
        }

        private object And()
        {
            object left = Comparison();
            while (Accept("&&")) { bool right = Boolean(Comparison()); left = Boolean(left) && right; }
            return left;
        }

        private object Comparison()
        {
            object left = Remainder();
            foreach (string op in new[] { "===", "!==", ">=", "<=", ">", "<" })
            {
                if (!Accept(op)) continue;
                BigInteger a = Number(left), b = Number(Remainder());
                return op switch { "===" => a == b, "!==" => a != b, ">=" => a >= b, "<=" => a <= b, ">" => a > b, _ => a < b };
            }
            return left;
        }

        private object Remainder()
        {
            object left = Unary();
            while (Accept("%")) { BigInteger divisor = Number(Unary()); left = Number(left) % divisor; }
            return left;
        }

        private object Unary()
        {
            if (Accept("!")) return !Boolean(Unary());
            if (Accept("("))
            {
                object inner = Or();
                Expect(")");
                return inner;
            }
            if (_next >= tokens.Count) throw new FormatException("Unexpected end of plural rule.");
            string token = tokens[_next++];
            if (token.StartsWith('#')) return BigInteger.Parse(token.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture);
            return token switch
            {
                "o.n" or "o.i" => n,
                "o.v" or "o.w" or "o.f" or "o.t" or "o.e" or "o.c" => BigInteger.Zero,
                _ => throw new FormatException("Unexpected plural rule operand."),
            };
        }
    }
}
