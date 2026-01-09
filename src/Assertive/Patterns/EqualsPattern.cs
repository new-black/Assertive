using System.Linq.Expressions;
using Assertive.Analyzers;
using Assertive.Helpers;
using Assertive.Interfaces;
using static Assertive.Expressions.ExpressionHelper;

namespace Assertive.Patterns
{
  internal class EqualsPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(FailedAssertion failedAssertion)
    {
      return failedAssertion.Expression.NodeType == ExpressionType.Equal
             || EqualityPattern.EqualsMethodShouldBeTrue(failedAssertion.Expression);
    }

    public ExpectedAndActual? TryGetFriendlyMessage(FailedAssertion assertion)
    {
      var left = EqualityPattern.GetLeftSide(assertion.Expression);

      var right = left != null ? EqualityPattern.GetRightSide(assertion.Expression, left) : null;

      if (right is { NodeType: ExpressionType.Convert } && right.Type == typeof(object))
      {
        right = ((UnaryExpression)right).Operand;
      }

      object? expected = right != null && IsConstantExpression(right) ? right : right?.ToValue();
      object? actual = left?.ToValue();
      string diff = "";

      // Check if both sides are strings and provide a smart diff
      if (left != null && right != null &&
          left.Type == typeof(string) && right.Type == typeof(string))
      {
        var leftValue = EvaluateExpression(left) as string;
        var rightValue = EvaluateExpression(right) as string;

        if (leftValue != null && rightValue != null && leftValue != rightValue
            && (leftValue.Length > 10 || rightValue.Length > 10))
        {
          diff = StringDiffHelper.GetStringDiff(leftValue, rightValue);
        }
      }

      return new()
      {
        Expected = $"{left}: {expected}",
        Actual = $"{left}: {actual}{diff}"
      };
    }

    private static string EscapeString(string s)
    {
      return s.Replace("\r", "\\r")
        .Replace("\n", "\\n")
        .Replace("\t", "\\t");
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = [];
  }
}
