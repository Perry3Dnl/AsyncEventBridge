namespace AsyncEventBridge;

/// <summary>
/// Marks a type whose compatible .NET events should receive generated one-shot async wait methods and async stream methods.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class GenerateAsyncEventsAttribute : Attribute
{
}
