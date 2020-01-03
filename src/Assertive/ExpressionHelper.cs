using System.Linq.Expressions;
using System.Text.RegularExpressions;

namespace Assertive
{
  internal static class ExpressionHelper
  {
    public static object EvaluateExpression(Expression expression)
    {
      var lambda = Expression.Lambda(expression).Compile(true);

      var value = lambda.DynamicInvoke();

      if (value is string s)
      {
        return "\"" + s + "\"";
      }

      return value;
    }
    
    private static readonly Regex _closureCleanup = new Regex(@"(value\(.*?<>.*?\)\.)", RegexOptions.Compiled);
    private static readonly Regex _indexPropertyRewrite = new Regex(@".get_Item\((.+?)\)", RegexOptions.Compiled);

    public static string SanitizeExpression(Expression expression)
    {
      return SanitizeExpressionString(expression.ToString());
    }
    
    public static string SanitizeExpressionString(string str)
    {
      if (str == null) return null;

      var result = _closureCleanup.Replace(str, "");

      result = _indexPropertyRewrite.Replace(result, "[$1]");

      return result;
    }

  }
}