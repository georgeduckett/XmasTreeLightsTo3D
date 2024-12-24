using System.Text.Json;

namespace TreeLightsWeb
{
    public static class Utilities
    {
        public static string ConvertCsvFileToJsonObject(string csvText)
        {
            var csv = new List<string[]>();
            var lines = csvText.Split(Environment.NewLine).Select(l => l.Trim()).ToArray();

            foreach (string line in lines)
                csv.Add(line.Split(',').Select(l => l.Trim()).ToArray());

            var properties = lines[0].Split(',').Select(f => f.Trim().Replace(" ", "")).ToArray();

            var listObjResult = new List<Dictionary<string, string>>();

            for (int i = 1; i < lines.Length; i++)
            {
                var objResult = new Dictionary<string, string>();
                for (int j = 0; j < properties.Length; j++)
                    objResult.Add(properties[j], csv[i][j]);

                listObjResult.Add(objResult);
            }

            return JsonSerializer.Serialize(listObjResult);
        }
    }
}