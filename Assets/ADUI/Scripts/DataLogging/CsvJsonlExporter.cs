using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

public class CsvJsonlExporter : MonoBehaviour
{
    public void AppendJsonl<T>(string directory, string fileName, T record)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.AppendAllText(path, JsonUtility.ToJson(record, false) + Environment.NewLine, Encoding.UTF8);
    }

    public void WriteJson<T>(string directory, string fileName, T record, bool pretty = true)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), JsonUtility.ToJson(record, pretty), Encoding.UTF8);
    }

    public void WriteCsv<T>(string directory, string fileName, IEnumerable<T> records)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        var fields = typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public);
        var builder = new StringBuilder();
        for (var i = 0; i < fields.Length; i++)
        {
            if (i > 0) builder.Append(",");
            builder.Append(Escape(fields[i].Name));
        }
        builder.AppendLine();

        foreach (var record in records)
        {
            for (var i = 0; i < fields.Length; i++)
            {
                if (i > 0) builder.Append(",");
                var value = fields[i].GetValue(record);
                builder.Append(Escape(FormatValue(value)));
            }
            builder.AppendLine();
        }

        File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
    }

    private string FormatValue(object value)
    {
        if (value == null) return "";
        if (value is float f) return f.ToString(CultureInfo.InvariantCulture);
        if (value is double d) return d.ToString(CultureInfo.InvariantCulture);
        if (value is bool b) return b ? "true" : "false";
        return value.ToString();
    }

    private string Escape(string value)
    {
        if (value == null) return "";
        var needsQuotes = value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r");
        if (!needsQuotes) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}

