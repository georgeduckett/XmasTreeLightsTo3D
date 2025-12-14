namespace StrangerThingsLights.Models
{
    public class LightsLayoutModel
    {
        public record LetterMapping(char StartLetter, ushort StartPosition, byte Count, int Increment);
        public LetterMapping[] LetterMappings { get; set; } = [];

        private int[] LetterIndexes = new int[26];

        public void Validate()
        {
            foreach (var mapping in LetterMappings)
            {
                for (var i = 0; i < mapping.Count; i++)
                {
                    var letter = (char)(mapping.StartLetter + i * mapping.Increment);
                    if (letter < 'a' || letter > 'z')
                    {
                        throw new ArgumentOutOfRangeException(nameof(LetterMappings), $"Letter '{letter}' is out of range. Must be between 'a' and 'z'. Mapping with start letter {mapping.StartLetter}.");
                    }
                    LetterIndexes[letter - 'a'] = mapping.StartPosition + i;
                }
            }
        }

        public int GetLetterLightIndex(char letter) => LetterIndexes[letter - 'a'];
    }
}
