using System.ComponentModel;

namespace Assertive.Mocking
{
  /// <summary>
  /// Maps a mocked interface type to a factory for its source-generated implementation. The
  /// Assertive.Mocking source generator registers each discovered mock here from a module
  /// initializer, so <c>A&lt;T&gt;()</c> resolves without reflection or <c>Activator</c>.
  /// </summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public static class MockFactoryRegistry
  {
    private static readonly Dictionary<Type, Func<object?[], object>> Factories = new();

    public static void Register(Type type, Func<object?[], object> factory) => Factories[type] = factory;

    public static bool TryCreate(Type type, out object mock)
    {
      if (Factories.TryGetValue(type, out var factory))
      {
        mock = factory(System.Array.Empty<object?>());
        return true;
      }
      mock = null!;
      return false;
    }

    public static T Create<T>(object?[] constructorArguments) where T : class
    {
      if (Factories.TryGetValue(typeof(T), out var factory))
      {
        return (T)factory(constructorArguments);
      }

      throw new InvalidOperationException(
        $"Assertive.Mocking: no generated mock found for '{typeof(T).FullName}'. " +
        "Ensure the Assertive.Mocking.Generators analyzer is referenced by this project and that " +
        $"'{typeof(T).Name}' is a (non-sealed) interface or class used in an A<{typeof(T).Name}>() call.");
    }
  }
}
