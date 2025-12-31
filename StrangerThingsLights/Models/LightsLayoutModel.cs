namespace StrangerThingsLights.Models
{
    public class LightsLayoutModel
    {
        public record LetterMapping(char StartLetter, ushort StartPosition, char EndLetter, int IndexIncrement);
        public LetterMapping[] LetterMappings { get; set; } = [];

        private int[] LetterIndexes = new int[26];

        public void Validate()
        {
            foreach (var mapping in LetterMappings)
            {
                var count = Math.Abs(mapping.EndLetter - mapping.StartLetter) + 1;
                var letterIncrement = mapping.EndLetter >= mapping.StartLetter ? 1 : -1;

                for (var i = 0; i < count; i++)
                {
                    var letter = (char)(mapping.StartLetter + i * letterIncrement);
                    if (letter < 'a' || letter > 'z')
                    {
                        throw new ArgumentOutOfRangeException(nameof(LetterMappings), $"Letter '{letter}' is out of range. Must be between 'a' and 'z'. Mapping with start letter {mapping.StartLetter}.");
                    }
                    LetterIndexes[letter - 'a'] = mapping.StartPosition + i * (mapping.IndexIncrement == 0 ? 1 : mapping.IndexIncrement);
                }
            }

            Console.WriteLine(string.Join(Environment.NewLine, LetterIndexes.Select((index, i) => $"{(char)('a' + i)}: {index}")));
        }

        public int GetLetterLightIndex(char letter) => LetterIndexes[letter - 'a'];
    }
}
