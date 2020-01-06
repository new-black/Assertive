using System;
using System.Globalization;
using System.Linq.Expressions;

namespace Assertive
{
  public class FriendlyMessageFormatter
  {
    public static string GetString(FormattableString formattableString)
    {
      if (formattableString == null) return string.Empty;

      var arguments = formattableString.GetArguments();

      for (var i = 0; i < arguments.Length; i++)
      {
        var a = arguments[i];
        
        if (a is Expression expression)
        {
          arguments[i] = ExpressionStringBuilder.ExpressionToString(expression);
        }
      }

      return string.Format(CultureInfo.InvariantCulture, formattableString.Format, arguments);
    }
  }
}