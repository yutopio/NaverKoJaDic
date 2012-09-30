using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml.Linq;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.WriteLine("Specify the word and the output dictionary file.");
            return;
        }

        // Parse startup arguments.
        var keyword = args[0];
        var dic = new JaKoDic();
        if (File.Exists(args[1])) dic.ReadXml(args[1]);

        // Search the word and change the database.
        foreach (var word in Extract(keyword))
        {
            var row = dic.Word.AddWordRow(word.ID, word.Reading, word.Hanja);
            var i = 1;
            foreach (var meanings in word.Meanings)
                dic.Meaning.AddMeaningRow(row, i++, meanings.POS, meanings.Description);
        }

        // Save back to the file.
        dic.WriteXml(args[1]);
    }

    static IEnumerable<Word> Extract(string keyword)
    {
        // Download dictionary page from Naver dictionary website.
        var url = "http://jpdic.naver.com/search.nhn?query={0}";
        var wc = new WebClient();
        var ret = Encoding.UTF8.GetString(
            wc.DownloadData(string.Format(url, HttpUtility.UrlEncode(keyword))));

        // Look for word search result.
        var i = ret.IndexOf("<dl class=\"entry_result\">");
        if (i == -1) yield break;

        // Just take out result listings.
        var j = ret.IndexOf("</dl>", i) + "</dl>".Length;
        ret = ret.Substring(i, j - i);

        // Image tags are not well formed in XML, so delete it. Additionally,
        // < and > in attribute values are treated as incorrect format by
        // XmlDocument class, so just remove by regular expressions.
        var regex = new Regex("<img [^>]+>");
        ret = regex.Replace(ret, m => "");
        regex = new Regex("<span class=\"jpAutoLink\"[^\\n]+</span>\">");
        ret = regex.Replace(ret, m => "<span>");

        // Parse it as an XML snippet.
        var doc = XElement.Parse(ret);
        foreach (var n in doc.Descendants().ToList())
            // Remove annotations of phonetic readings in Japanese.
            if (n.Name == "sup" && n.Attribute("class").Value == "huri") n.Remove();

        // Construct the result data.
        Word current = null;
        foreach (var n in doc.Elements())
        {
            if (n.Name == "dt") // Word title.
            {
                if (current != null) yield return current;

                // Extract word ID on Naver dictionary and Korean reading.
                var link = n.Descendants().First();
                Debug.Assert(link.Name == "a");
                var id = int.Parse(link.Attribute("href").Value.Replace("/entry_krjp.nhn?entryId=", ""));
                var text = link.Value;

                // If it can be written in Chinese letters, also take it.
                var start = n.Nodes().FirstOrDefault(x => x.ToString().Trim() == "[");
                string hanja = null;
                if (start != null)
                {
                    var node = start.NextNode;
                    hanja = "";
                    do
                    {
                        hanja += (node as XElement).Value;
                        node = node.NextNode;
                    } while (node.ToString().Trim() != "]");
                }

                // Make a new instance of Word.
                current = new Word { ID = id, Reading = text, Hanja = hanja };
            }
            else if (n.Name == "dd")    // Word meaning.
            {
                n.Nodes().First().Remove(); // Remove counting bullet.
                n.Nodes().First().Remove(); // Remove period.

                // Take POS annotation and remove it.
                var pos = n.Elements().First().Value;
                pos = pos.Substring(1, pos.Length - 2);
                n.Nodes().First().Remove();

                // All the rests are word's explanations.
                var desc = n.Value.Trim();
                current.Meanings.Add(new Meaning { POS = pos, Description = desc });
            }
        }
        if (current != null) yield return current;
    }
}

public class Word
{
    public Word()
    {
        this.Meanings = new List<Meaning>();
    }

    public int ID { get; set; }
    public string Reading { get; set; }
    public string Hanja { get; set; }
    public List<Meaning> Meanings { get; private set; }

    public override string ToString()
    {
        return string.Format("{0}\t{1}\t{2}\n{3}", ID, Reading, Hanja,
            string.Join("\n", Meanings.Select(x => x.ToString()).ToArray()));
    }
}

public class Meaning
{
    string pos = "";
    public string POS
    {
        get { return pos; }
        set
        {
            // Prepare conversion table.
            var table = new[,] {
                { "명사", "名詞"},  // Noun
                { "대명사", "代名詞" },   // Pronoun
                { "명사·하다형 자동사", "名詞 / 自動詞" },   // Noun / intransitive verb
                { "명사·하다형 타동사", "名詞 / 他動詞" },   // Noun / transitive verb
            };

            for (var i = 0; i < table.GetLength(0); i++)
                if (table[i, 0] == value)
                {
                    pos = table[i, 1];
                    return;
                }
            pos = value;
        }
    }

    public string Description { get; set; }

    public override string ToString()
    {
        return string.Format("[{0}] {1}", POS, Description);
    }
}
