using System;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>Names of new features: the wanted one, " (2)", " (3)" ... if it is taken.</summary>
    public static class FeatureNames
    {
        /// <summary>
        /// The name a feature called <paramref name="current"/> gets when it should be called
        /// <paramref name="wanted"/>. Its own current name is no clash: a feature already
        /// renamed to "X (2)" because "X" was taken stays "X (2)" when it is renamed to "X" again.
        /// </summary>
        /// <param name="taken">Is there a feature with this name in the document (the feature itself included)?</param>
        public static string Free(string wanted, string current, Func<string, bool> taken)
        {
            string name = wanted;
            for (int i = 2; name != current && taken(name); i++) name = $"{wanted} ({i})";
            return name;
        }
    }
}
