namespace Assertive
{
  internal static class StringQuoter
  {
    public static object? Quote(object? o)
    {
      if (o is string s)
      {
        return "\"" + s + "\"";
      }

      return o;
    }
  }
}