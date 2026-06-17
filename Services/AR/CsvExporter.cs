using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PlugINPRO.Services.AR
{
    public class CsvExporter
    {
        private const string Separator = ",";
        public void Export(
           string filePath,
           IEnumerable<string> headers,
           List<List<string>> rows)
        {
            var sb = new StringBuilder();

            // Заголовок
            sb.AppendLine(string.Join(Separator, headers));

            // Строки
            foreach (var row in rows)
            {
                if (row == null) continue; // <-- защита от null
                sb.AppendLine(string.Join(Separator, row));
            }
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }
    }
}
