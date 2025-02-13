using System.Reflection.Metadata.Ecma335;

namespace GroqSharp.Tools
{
    public interface IGroqToolParameter
    {
        GroqToolParameterType Type { get; }

        string Description { get; }

        IEnumerable<string>? Enum { get { return null; } }

        bool Required { get; }

        IDictionary<string, IGroqToolParameter> Properties { get; }

        object ToJsonSerializableObject();
    }
}