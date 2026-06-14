using System;

namespace Assertive.Mocking
{
  /// <summary>
  /// Method-group arrangement: <c>Any(mock.Method)</c> arranges a call to <c>Method</c> for ANY
  /// arguments, without writing placeholder/matcher arguments. The method group converts to a
  /// delegate whose <see cref="Delegate.Target"/> is the mock and <see cref="Delegate.Method"/>
  /// names the method, so this needs no source generation — just reflection on the delegate.
  /// </summary>
  public static partial class Mock
  {
    private static (MockBase Mock, string Method) Resolve(Delegate method)
    {
      if (method.Target is not IMockObject mock)
      {
        throw new InvalidOperationException(
          "Assertive.Mocking: Any(...) expects a mock method group, e.g. Any(greeter.Greet).");
      }

      return (mock.Core, method.Method.Name);
    }

    // A representative set of arities. A real implementation would cover Func/Action up to 16
    // type parameters; the method group's signature selects the right overload, and C# infers
    // the type arguments from it.
    /// <summary>Arranges a zero-argument value method for any call.</summary>
    public static ValueArrange<TResult> Any<TResult>(Func<TResult> method)
    {
      var (mock, name) = Resolve(method);
      return new ValueArrange<TResult>(mock, name);
    }

    /// <summary>Arranges a one-argument value method for any arguments.</summary>
    public static ValueArrange<T1, TResult> Any<T1, TResult>(Func<T1, TResult> method)
    {
      var (mock, name) = Resolve(method);
      return new ValueArrange<T1, TResult>(mock, name);
    }

    /// <summary>Arranges a two-argument value method for any arguments.</summary>
    public static ValueArrange<T1, T2, TResult> Any<T1, T2, TResult>(Func<T1, T2, TResult> method)
    {
      var (mock, name) = Resolve(method);
      return new ValueArrange<T1, T2, TResult>(mock, name);
    }

    /// <summary>Arranges a zero-argument void method for any call.</summary>
    public static VoidArrange Any(Action method)
    {
      var (mock, name) = Resolve(method);
      return new VoidArrange(mock, name);
    }

    /// <summary>Arranges a one-argument void method for any arguments.</summary>
    public static VoidArrange<T1> Any<T1>(Action<T1> method)
    {
      var (mock, name) = Resolve(method);
      return new VoidArrange<T1>(mock, name);
    }

    /// <summary>Arranges a three-argument value method for any arguments.</summary>
    public static ValueArrange<T1, T2, T3, TResult> Any<T1, T2, T3, TResult>(Func<T1, T2, T3, TResult> method)
    {
      var (mock, name) = Resolve(method);
      return new ValueArrange<T1, T2, T3, TResult>(mock, name);
    }

    /// <summary>Arranges a four-argument value method for any arguments.</summary>
    public static ValueArrange<T1, T2, T3, T4, TResult> Any<T1, T2, T3, T4, TResult>(Func<T1, T2, T3, T4, TResult> method)
    {
      var (mock, name) = Resolve(method);
      return new ValueArrange<T1, T2, T3, T4, TResult>(mock, name);
    }

    /// <summary>Arranges a two-argument void method for any arguments.</summary>
    public static VoidArrange<T1, T2> Any<T1, T2>(Action<T1, T2> method)
    {
      var (mock, name) = Resolve(method);
      return new VoidArrange<T1, T2>(mock, name);
    }

    /// <summary>Arranges a three-argument void method for any arguments.</summary>
    public static VoidArrange<T1, T2, T3> Any<T1, T2, T3>(Action<T1, T2, T3> method)
    {
      var (mock, name) = Resolve(method);
      return new VoidArrange<T1, T2, T3>(mock, name);
    }
  }

  /// <summary>Arranges a parameterless value method matched for any (i.e. no) arguments.</summary>
  public readonly struct ValueArrange<TResult>
  {
    private readonly MockBase _mock;
    private readonly string _method;
    internal ValueArrange(MockBase mock, string method) { _mock = mock; _method = method; }

    /// <summary>Configures the method to return <paramref name="value"/> on every call.</summary>
    public void Returns(TResult value) => _mock.AddSetup(_method, _ => true, _ => value);
    /// <summary>Configures the method to invoke <paramref name="impl"/> and return its result on every call.</summary>
    public void Returns(Func<TResult> impl) => _mock.AddSetup(_method, _ => true, _ => impl());
    /// <summary>Configures the method to throw <paramref name="exception"/> on every call.</summary>
    public void Throws(Exception exception) => _mock.AddSetup(_method, _ => true, _ => new MockFault(exception));

    /// <summary>
    /// Arranges sequential return values: each call consumes the next value; after exhaustion
    /// the last value is returned repeatedly.
    /// </summary>
    public void ReturnsMany(params TResult[] values)
    {
      var idx = 0;
      _mock.AddSetup(_method, _ => true, _ => values[Math.Min(idx++, values.Length - 1)]);
    }

    /// <summary>Behavior-attach hook used by the async <c>Returns</c> extensions.</summary>
    internal void Attach(Func<object?[], object?> behavior) => _mock.AddSetup(_method, _ => true, behavior);
  }

  /// <summary>Arranges a one-argument value method for any argument, with access to that argument.</summary>
  public readonly struct ValueArrange<T1, TResult>
  {
    private readonly MockBase _mock;
    private readonly string _method;
    internal ValueArrange(MockBase mock, string method) { _mock = mock; _method = method; }

    /// <summary>Configures the method to return <paramref name="value"/> on every call.</summary>
    public void Returns(TResult value) => _mock.AddSetup(_method, _ => true, _ => value);
    /// <summary>Configures the method to invoke <paramref name="impl"/> with the call argument and return its result.</summary>
    public void Returns(Func<T1, TResult> impl) => _mock.AddSetup(_method, _ => true, args => impl((T1)args[0]!));
    /// <summary>Configures the method to throw <paramref name="exception"/> on every call.</summary>
    public void Throws(Exception exception) => _mock.AddSetup(_method, _ => true, _ => new MockFault(exception));

    /// <summary>
    /// Arranges sequential return values: each call consumes the next value; after exhaustion
    /// the last value is returned repeatedly.
    /// </summary>
    public void ReturnsMany(params TResult[] values)
    {
      var idx = 0;
      _mock.AddSetup(_method, _ => true, _ => values[Math.Min(idx++, values.Length - 1)]);
    }

    internal void Attach(Func<object?[], object?> behavior) => _mock.AddSetup(_method, _ => true, behavior);
  }

  /// <summary>Arranges a two-argument value method for any arguments, with access to both.</summary>
  public readonly struct ValueArrange<T1, T2, TResult>
  {
    private readonly MockBase _mock;
    private readonly string _method;
    internal ValueArrange(MockBase mock, string method) { _mock = mock; _method = method; }

    /// <summary>Configures the method to return <paramref name="value"/> on every call.</summary>
    public void Returns(TResult value) => _mock.AddSetup(_method, _ => true, _ => value);
    /// <summary>Configures the method to invoke <paramref name="impl"/> with both call arguments and return its result.</summary>
    public void Returns(Func<T1, T2, TResult> impl) => _mock.AddSetup(_method, _ => true, args => impl((T1)args[0]!, (T2)args[1]!));
    /// <summary>Configures the method to throw <paramref name="exception"/> on every call.</summary>
    public void Throws(Exception exception) => _mock.AddSetup(_method, _ => true, _ => new MockFault(exception));

    /// <summary>
    /// Arranges sequential return values: each call consumes the next value; after exhaustion
    /// the last value is returned repeatedly.
    /// </summary>
    public void ReturnsMany(params TResult[] values)
    {
      var idx = 0;
      _mock.AddSetup(_method, _ => true, _ => values[Math.Min(idx++, values.Length - 1)]);
    }

    internal void Attach(Func<object?[], object?> behavior) => _mock.AddSetup(_method, _ => true, behavior);
  }

  /// <summary>Arranges a three-argument value method for any arguments, with access to all three.</summary>
  public readonly struct ValueArrange<T1, T2, T3, TResult>
  {
    private readonly MockBase _mock;
    private readonly string _method;
    internal ValueArrange(MockBase mock, string method) { _mock = mock; _method = method; }

    /// <summary>Configures the method to return <paramref name="value"/> on every call.</summary>
    public void Returns(TResult value) => _mock.AddSetup(_method, _ => true, _ => value);
    /// <summary>Configures the method to invoke <paramref name="impl"/> with all three call arguments and return its result.</summary>
    public void Returns(Func<T1, T2, T3, TResult> impl) => _mock.AddSetup(_method, _ => true, args => impl((T1)args[0]!, (T2)args[1]!, (T3)args[2]!));
    /// <summary>Configures the method to throw <paramref name="exception"/> on every call.</summary>
    public void Throws(Exception exception) => _mock.AddSetup(_method, _ => true, _ => new MockFault(exception));

    /// <summary>
    /// Arranges sequential return values: each call consumes the next value; after exhaustion
    /// the last value is returned repeatedly.
    /// </summary>
    public void ReturnsMany(params TResult[] values)
    {
      var idx = 0;
      _mock.AddSetup(_method, _ => true, _ => values[Math.Min(idx++, values.Length - 1)]);
    }

    internal void Attach(Func<object?[], object?> behavior) => _mock.AddSetup(_method, _ => true, behavior);
  }

  /// <summary>Arranges a four-argument value method for any arguments, with access to all four.</summary>
  public readonly struct ValueArrange<T1, T2, T3, T4, TResult>
  {
    private readonly MockBase _mock;
    private readonly string _method;
    internal ValueArrange(MockBase mock, string method) { _mock = mock; _method = method; }

    /// <summary>Configures the method to return <paramref name="value"/> on every call.</summary>
    public void Returns(TResult value) => _mock.AddSetup(_method, _ => true, _ => value);
    /// <summary>Configures the method to invoke <paramref name="impl"/> with all four call arguments and return its result.</summary>
    public void Returns(Func<T1, T2, T3, T4, TResult> impl) => _mock.AddSetup(_method, _ => true, args => impl((T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!));
    /// <summary>Configures the method to throw <paramref name="exception"/> on every call.</summary>
    public void Throws(Exception exception) => _mock.AddSetup(_method, _ => true, _ => new MockFault(exception));

    /// <summary>
    /// Arranges sequential return values: each call consumes the next value; after exhaustion
    /// the last value is returned repeatedly.
    /// </summary>
    public void ReturnsMany(params TResult[] values)
    {
      var idx = 0;
      _mock.AddSetup(_method, _ => true, _ => values[Math.Min(idx++, values.Length - 1)]);
    }

    internal void Attach(Func<object?[], object?> behavior) => _mock.AddSetup(_method, _ => true, behavior);
  }

  /// <summary>
  /// Unwrapped async <c>Returns</c> for <c>Any(methodGroup)</c>: when the method returns
  /// <c>Task&lt;T&gt;</c>/<c>ValueTask&lt;T&gt;</c>, you pass the bare <c>T</c> and it's wrapped in a
  /// completed task. More specific than <c>Returns(TResult)</c>, so <c>.Returns(value)</c> picks
  /// this; <c>.Returns(Task.FromResult(value))</c> still resolves to the general overload.
  /// </summary>
  public static class AsyncArrangeExtensions
  {
    /// <summary>Configures a <c>Task&lt;T&gt;</c>-returning method to complete with <paramref name="value"/>.</summary>
    public static void Returns<T>(this ValueArrange<Task<T>> arrange, T value) => arrange.Attach(_ => Task.FromResult(value));
    /// <inheritdoc cref="Returns{T}(ValueArrange{Task{T}}, T)"/>
    public static void Returns<T1, T>(this ValueArrange<T1, Task<T>> arrange, T value) => arrange.Attach(_ => Task.FromResult(value));
    /// <inheritdoc cref="Returns{T}(ValueArrange{Task{T}}, T)"/>
    public static void Returns<T1, T2, T>(this ValueArrange<T1, T2, Task<T>> arrange, T value) => arrange.Attach(_ => Task.FromResult(value));
    /// <inheritdoc cref="Returns{T}(ValueArrange{Task{T}}, T)"/>
    public static void Returns<T1, T2, T3, T>(this ValueArrange<T1, T2, T3, Task<T>> arrange, T value) => arrange.Attach(_ => Task.FromResult(value));
    /// <inheritdoc cref="Returns{T}(ValueArrange{Task{T}}, T)"/>
    public static void Returns<T1, T2, T3, T4, T>(this ValueArrange<T1, T2, T3, T4, Task<T>> arrange, T value) => arrange.Attach(_ => Task.FromResult(value));

    /// <summary>Configures a <c>ValueTask&lt;T&gt;</c>-returning method to complete with <paramref name="value"/>.</summary>
    public static void Returns<T>(this ValueArrange<ValueTask<T>> arrange, T value) => arrange.Attach(_ => new ValueTask<T>(value));
    /// <inheritdoc cref="Returns{T}(ValueArrange{ValueTask{T}}, T)"/>
    public static void Returns<T1, T>(this ValueArrange<T1, ValueTask<T>> arrange, T value) => arrange.Attach(_ => new ValueTask<T>(value));
    /// <inheritdoc cref="Returns{T}(ValueArrange{ValueTask{T}}, T)"/>
    public static void Returns<T1, T2, T>(this ValueArrange<T1, T2, ValueTask<T>> arrange, T value) => arrange.Attach(_ => new ValueTask<T>(value));
    /// <inheritdoc cref="Returns{T}(ValueArrange{ValueTask{T}}, T)"/>
    public static void Returns<T1, T2, T3, T>(this ValueArrange<T1, T2, T3, ValueTask<T>> arrange, T value) => arrange.Attach(_ => new ValueTask<T>(value));
    /// <inheritdoc cref="Returns{T}(ValueArrange{ValueTask{T}}, T)"/>
    public static void Returns<T1, T2, T3, T4, T>(this ValueArrange<T1, T2, T3, T4, ValueTask<T>> arrange, T value) => arrange.Attach(_ => new ValueTask<T>(value));
  }

  /// <summary>Arranges a parameterless void method for any (i.e. no) arguments.</summary>
  public readonly struct VoidArrange
  {
    private readonly MockBase _mock;
    private readonly string _method;
    internal VoidArrange(MockBase mock, string method) { _mock = mock; _method = method; }

    /// <summary>Configures the method to throw <paramref name="exception"/> on every call.</summary>
    public void Throws(Exception exception) => _mock.AddSetup(_method, _ => true, _ => new MockFault(exception));
    /// <summary>Configures the method to throw a new <typeparamref name="TException"/> on every call.</summary>
    public void Throws<TException>() where TException : Exception, new() => _mock.AddSetup(_method, _ => true, _ => new MockFault(new TException()));
    /// <summary>Configures the method to run <paramref name="callback"/> on every call.</summary>
    public void Does(Action callback) => _mock.AddSetup(_method, _ => true, _ => { callback(); return null; });
  }

  /// <summary>Arranges a one-argument void method for any argument, with access to that argument.</summary>
  public readonly struct VoidArrange<T1>
  {
    private readonly MockBase _mock;
    private readonly string _method;
    internal VoidArrange(MockBase mock, string method) { _mock = mock; _method = method; }

    /// <summary>Configures the method to throw <paramref name="exception"/> on every call.</summary>
    public void Throws(Exception exception) => _mock.AddSetup(_method, _ => true, _ => new MockFault(exception));
    /// <summary>Configures the method to throw a new <typeparamref name="TException"/> on every call.</summary>
    public void Throws<TException>() where TException : Exception, new() => _mock.AddSetup(_method, _ => true, _ => new MockFault(new TException()));
    /// <summary>Configures the method to run <paramref name="callback"/> with the call argument on every call.</summary>
    public void Does(Action<T1> callback) => _mock.AddSetup(_method, _ => true, args => { callback((T1)args[0]!); return null; });
  }

  /// <summary>Arranges a two-argument void method for any arguments, with access to both.</summary>
  public readonly struct VoidArrange<T1, T2>
  {
    private readonly MockBase _mock;
    private readonly string _method;
    internal VoidArrange(MockBase mock, string method) { _mock = mock; _method = method; }

    /// <summary>Configures the method to throw <paramref name="exception"/> on every call.</summary>
    public void Throws(Exception exception) => _mock.AddSetup(_method, _ => true, _ => new MockFault(exception));
    /// <summary>Configures the method to throw a new <typeparamref name="TException"/> on every call.</summary>
    public void Throws<TException>() where TException : Exception, new() => _mock.AddSetup(_method, _ => true, _ => new MockFault(new TException()));
    /// <summary>Configures the method to run <paramref name="callback"/> with both call arguments on every call.</summary>
    public void Does(Action<T1, T2> callback) => _mock.AddSetup(_method, _ => true, args => { callback((T1)args[0]!, (T2)args[1]!); return null; });
  }

  /// <summary>Arranges a three-argument void method for any arguments, with access to all three.</summary>
  public readonly struct VoidArrange<T1, T2, T3>
  {
    private readonly MockBase _mock;
    private readonly string _method;
    internal VoidArrange(MockBase mock, string method) { _mock = mock; _method = method; }

    /// <summary>Configures the method to throw <paramref name="exception"/> on every call.</summary>
    public void Throws(Exception exception) => _mock.AddSetup(_method, _ => true, _ => new MockFault(exception));
    /// <summary>Configures the method to throw a new <typeparamref name="TException"/> on every call.</summary>
    public void Throws<TException>() where TException : Exception, new() => _mock.AddSetup(_method, _ => true, _ => new MockFault(new TException()));
    /// <summary>Configures the method to run <paramref name="callback"/> with all three call arguments on every call.</summary>
    public void Does(Action<T1, T2, T3> callback) => _mock.AddSetup(_method, _ => true, args => { callback((T1)args[0]!, (T2)args[1]!, (T3)args[2]!); return null; });
  }
}
