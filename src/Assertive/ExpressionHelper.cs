using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
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
    private static readonly Regex _arrayLengthRewrite = new Regex(@"ArrayLength\((.+?)\)", RegexOptions.Compiled);

    public static Expression GetInstanceOfMethodCall(MethodCallExpression methodCallExpression)
    {
      var instance = methodCallExpression.Object;

      var isExtensionMethod = methodCallExpression.Method.IsDefined(typeof(ExtensionAttribute), false);

      if (isExtensionMethod)
      {
        instance = methodCallExpression.Arguments.First();
      }

      return instance;
    }
  }
}