using System.ComponentModel;

namespace Assertive.Mocking.Runtime
{
  /// <summary>
  /// Maps a wrapped interface type to a factory for its source-generated spy implementation.
  /// The source generator registers each type discovered in a <c>Wrap&lt;T&gt;()</c> call from a
  /// module initializer, so wrapping is reflection-free and AOT-safe.
  /// </summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public static class WrapFactoryRegistry
  {
    private static readonly Dictionary<Type, Func<object, object>> Factories = new();

    public static void Register(Type type, Func<object, object> factory) => Factories[type] = factory;

    public static T Create<T>(T wrapped) where T : class
    {
      if (Factories.TryGetValue(typeof(T), out var factory))
      {
        return (T)factory(wrapped);
      }

      throw new InvalidOperationException(
        $"Assertive.Mocking: no generated wrapper found for '{typeof(T).FullName}'. " +
        "Ensure the Assertive.Mocking.Generators analyzer is referenced by this project and that " +
        $"'{typeof(T).Name}' is a non-generic interface used in a Wrap<{typeof(T).Name}>() call.");
    }
  }
}
