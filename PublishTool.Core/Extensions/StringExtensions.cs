namespace PublishTool.Extensions;

public static class StringExtensions
{
    extension(string? value)
    {
        public bool IsNullOrEmpty => string.IsNullOrEmpty(value);
        public bool IsNotNullOrEmpty => !string.IsNullOrEmpty(value);
        public bool IsNullOrWhiteSpace => string.IsNullOrWhiteSpace(value);
        public bool IsNotNullOrWhiteSpace => !string.IsNullOrWhiteSpace(value);
    }

    extension(string value)
    {
        public bool IsEmpty => string.IsNullOrEmpty(value);
        public bool IsEmptyOrWhiteSpace => string.IsNullOrWhiteSpace(value);
    }
}