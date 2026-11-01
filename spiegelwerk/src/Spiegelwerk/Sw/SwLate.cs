using System;
using System.Reflection;

namespace Spiegelwerk.Sw
{
    /// <summary>
    /// Late-bound access (IDispatch) to API members that only newer SOLIDWORKS versions have.
    /// The add-in is compiled against the 2019 interop; a member that the running version
    /// lacks simply yields null/false instead of breaking the build or the run.
    /// </summary>
    internal static class SwLate
    {
        public static object? Get(object com, string property)
        {
            try
            {
                return com.GetType().InvokeMember(property, BindingFlags.GetProperty, null, com, null);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Sets a property; true if it exists and took the value.</summary>
        public static bool Set(object com, string property, object value)
        {
            try
            {
                com.GetType().InvokeMember(property, BindingFlags.SetProperty, null, com, new[] { value });
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Calls a method and returns its result; null if the member is missing or failed (or returned null).</summary>
        public static object? Invoke(object com, string method, params object[] args)
        {
            try
            {
                return com.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, com, args);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Calls a method with by-reference (out) parameters: those marked in <paramref name="byRef"/>
        /// come back in <paramref name="args"/>. Returns the method's result; null if the member is
        /// missing or failed – then <paramref name="args"/> may be unchanged.
        /// </summary>
        public static object? InvokeOut(object com, string method, object?[] args, bool[] byRef)
        {
            if (byRef.Length != args.Length) throw new ArgumentException("Für jedes Argument ist anzugeben, ob es als Referenz übergeben wird.", nameof(byRef));
            if (args.Length == 0) return Invoke(com, method);
            try
            {
                var modifier = new ParameterModifier(args.Length);
                for (int i = 0; i < byRef.Length; i++) modifier[i] = byRef[i];
                return com.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, com, args, new[] { modifier }, null, null);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Calls a method; true if it exists and did not return false.</summary>
        public static bool Call(object com, string method, params object[] args)
        {
            try
            {
                object? result = com.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, com, args);
                return !(result is bool ok) || ok;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
