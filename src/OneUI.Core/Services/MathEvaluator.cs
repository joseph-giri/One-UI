using System.Globalization;

namespace OneUI.Core.Services;

/// <summary>
/// Safe recursive-descent mathematical expression evaluator for Spotlight instant calculations.
/// Supports +, -, *, /, %, ^, unary +/-, parentheses, and decimal numbers.
/// </summary>
public static class MathEvaluator
{
    public static bool TryEvaluate(string? expression, out double result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        // Require at least one operator or parentheses so plain numbers don't trigger calculator
        var trimmed = expression.Trim();
        bool hasOperator = false;
        bool hasDigit = false;
        foreach (var ch in trimmed)
        {
            if (char.IsDigit(ch)) hasDigit = true;
            else if (ch is '+' or '-' or '*' or '/' or '%' or '^' or '(' or ')') hasOperator = true;
            else if (!char.IsWhiteSpace(ch) && ch != '.') return false;
        }

        if (!hasDigit || !hasOperator)
            return false;

        try
        {
            var parser = new Parser(trimmed);
            var value = parser.ParseExpression();
            parser.SkipWhitespace();
            if (!parser.IsAtEnd || double.IsNaN(value) || double.IsInfinity(value))
                return false;

            result = value;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string FormatResult(double value)
    {
        if (Math.Abs(value - Math.Round(value)) < 1e-10 && Math.Abs(value) < 1e15)
        {
            return Math.Round(value).ToString("N0", CultureInfo.InvariantCulture);
        }

        return Math.Round(value, 8).ToString("G12", CultureInfo.InvariantCulture);
    }

    private sealed class Parser(string input)
    {
        private int _pos;

        public bool IsAtEnd => _pos >= input.Length;

        public void SkipWhitespace()
        {
            while (_pos < input.Length && char.IsWhiteSpace(input[_pos]))
                _pos++;
        }

        public double ParseExpression()
        {
            var left = ParseTerm();
            while (true)
            {
                SkipWhitespace();
                if (_pos < input.Length && input[_pos] == '+')
                {
                    _pos++;
                    left += ParseTerm();
                }
                else if (_pos < input.Length && input[_pos] == '-')
                {
                    _pos++;
                    left -= ParseTerm();
                }
                else
                {
                    break;
                }
            }

            return left;
        }

        private double ParseTerm()
        {
            var left = ParsePower();
            while (true)
            {
                SkipWhitespace();
                if (_pos < input.Length && input[_pos] == '*')
                {
                    _pos++;
                    left *= ParsePower();
                }
                else if (_pos < input.Length && input[_pos] == '/')
                {
                    _pos++;
                    var divisor = ParsePower();
                    if (Math.Abs(divisor) < 1e-15)
                        throw new DivideByZeroException();
                    left /= divisor;
                }
                else if (_pos < input.Length && input[_pos] == '%')
                {
                    _pos++;
                    var divisor = ParsePower();
                    if (Math.Abs(divisor) < 1e-15)
                        throw new DivideByZeroException();
                    left %= divisor;
                }
                else
                {
                    break;
                }
            }

            return left;
        }

        private double ParsePower()
        {
            var baseVal = ParseUnary();
            SkipWhitespace();
            if (_pos < input.Length && input[_pos] == '^')
            {
                _pos++;
                var exponent = ParsePower(); // right-associative
                return Math.Pow(baseVal, exponent);
            }

            return baseVal;
        }

        private double ParseUnary()
        {
            SkipWhitespace();
            if (_pos < input.Length && input[_pos] == '+')
            {
                _pos++;
                return ParseUnary();
            }
            if (_pos < input.Length && input[_pos] == '-')
            {
                _pos++;
                return -ParseUnary();
            }

            return ParsePrimary();
        }

        private double ParsePrimary()
        {
            SkipWhitespace();
            if (_pos < input.Length && input[_pos] == '(')
            {
                _pos++;
                var value = ParseExpression();
                SkipWhitespace();
                if (_pos >= input.Length || input[_pos] != ')')
                    throw new FormatException("Missing closing parenthesis");
                _pos++;
                return value;
            }

            var start = _pos;
            bool seenDot = false;
            while (_pos < input.Length && (char.IsDigit(input[_pos]) || (input[_pos] == '.' && !seenDot)))
            {
                if (input[_pos] == '.') seenDot = true;
                _pos++;
            }

            if (_pos == start)
                throw new FormatException("Expected number");

            return double.Parse(input.AsSpan(start, _pos - start), CultureInfo.InvariantCulture);
        }
    }
}
