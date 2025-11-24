using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using Assertive.Expressions;
using Assertive.Helpers;

namespace Assertive.Analyzers
{
  internal class FriendlyMessageFormatter
  {
    public static string? GetString(FormattableString? formattableString, HashSet<Expression> evaluatedExpressions)
    {
      if (formattableString == null) return null;

      var arguments = formattableString.GetArguments();

      for (var i = 0; i < arguments.Length; i++)
      {
        var a = arguments[i];

        if (a is Expression expression)
        {
          arguments[i] = ExpressionHelper.ExpressionToString(expression);
        }
        else if (a is UnquotedExpression unquotedExpression)
        {
          arguments[i] = ExpressionHelper.ExpressionToString(unquotedExpression.Expression, allowQuotation: false);
        }
        else if (a is FormattableString innerFormattableString)
        {
          arguments[i] = GetString(innerFormattableString, evaluatedExpressions);
        }
        else if (a is IEnumerable<FormattableString> formattableStrings)
        {
          arguments[i] = string.Join(Environment.NewLine, formattableStrings.Select(f => GetString(f, evaluatedExpressions)));
        }
        else if (a is null)
        {
          arguments[i] = "null";
        }
        else if (a is ExpressionValue expressionValue)
        {
          var value = ExpressionHelper.EvaluateExpression(expressionValue.Expression);
          evaluatedExpressions.Add(expressionValue.Expression);
          arguments[i] = Serializer.Serialize(value);
        }
        else if (a is string s)
        {
          arguments[i] = s;
        }
        else if (a is SerializerResult)
        {
          arguments[i] = a.ToString();
        }
        else
        {
          arguments[i] = Serializer.Serialize(arguments[i]);
        }
      }

      return string.Format(CultureInfo.InvariantCulture, formattableString.Format, arguments);
    }
  }
}