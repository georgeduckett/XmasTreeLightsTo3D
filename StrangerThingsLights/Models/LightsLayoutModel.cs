namespace StrangerThingsLights.Models
{
    public class LightsLayoutModel
    {
        public record LetterMapping(char Letter, ushort Position);
        public LetterMapping[] LetterMappings { get; set; } = [];

        private int[] LetterIndexes = new int[26];

        public void Validate()
        {
            // TODO: Handle some letter sequences going backwards
            foreach (var mapping in LetterMappings)
            {
                if (mapping.Letter < 'a' || mapping.Letter > 'z')
                {
                    throw new ArgumentOutOfRangeException(nameof(LetterMappings), $"Letter '{mapping.Letter}' is out of range. Must be between 'a' and 'z'.");
                }
                LetterIndexes[mapping.Letter - 'a'] = mapping.Position;
            }

            for (var letter = 'a'; letter <= 'z'; letter++)
            {
                if (!LetterMappings.Any(m => m.Letter == letter))
                {
                    // We don't have a letter mapping for this one, so assume it's sequential (see what index the previous letter had and add 1)
                    LetterIndexes[letter - 'a'] = LetterIndexes[letter - 'a' - 1] + 1;
                }
            }
        }

        public int GetLetterLightIndex(char letter) => LetterIndexes[letter - 'a'];
    }
}
