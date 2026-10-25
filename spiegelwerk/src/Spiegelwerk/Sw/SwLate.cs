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
