using System;

namespace Assertive.Expressions
{
  internal class ExpressionEnumValue
  {
    private readonly Type _enumType;
    private readonly object _value;

    public ExpressionEnumValue(Type enumType, object value)
    {
      _enumType = enumType;
      _value = value;
    }

    public override string ToString()
    {
      return _enumType.Name + "." +
             Enum.ToObject(_enumType, _value);
    }
  }
}