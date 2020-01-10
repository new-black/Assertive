using System;
using System.Globalization;
using System.Linq.Expressions;

namespace Assertive
{
  internal class FriendlyMessageFormatter
  {
    public static string GetString(FormattableString formattableString)
    {
      if (formattableString == null) return null;

      var arguments = formattableString.GetArguments();

      for (var i = 0; i < arguments.Length; i++)
      {
        var a = arguments[i];
        
        if (a is Expression expression)
        {
          arguments[i] = ExpressionStringBuilder.ExpressionToString(expression);
        }
        else if (a is FormattableString innerFormattableString)
        {
          arguments[i] = GetString(innerFormattableString);
        }
        else if (a is null)
        {
          arguments[i] = "null";
        }
      }

      return string.Format(CultureInfo.InvariantCulture, formattableString.Format, arguments);
    }
  }
}