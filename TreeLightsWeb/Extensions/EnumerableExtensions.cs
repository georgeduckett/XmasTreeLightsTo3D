namespace TreeLightsWeb.Extensions
{
    public static class EnumerableExtensions
    {
        public static T RandomElement<T>(this IEnumerable<T> source,
                                 Random rng)
        {
            T? current = default;
            int count = 0;
            foreach (T element in source)
            {
                count++;
                if (rng.Next(count) == 0)
                {
                    current = element;
                }
            }
            if (count == 0)
            {
                throw new InvalidOperationException("Sequence was empty");
            }
            return current!;
        }
        public static Queue<T> ToQueue<T>(this IEnumerable<T> source)
        {
            return new Queue<T>(source);
        }
    }
}
