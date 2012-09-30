using System;
using System.Collections.Generic;
using System.Data;
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

    static string GetPage(string keyword)
    {
        var dir = Environment.ExpandEnvironmentVariables(@"%TEMP%\naver");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        // Restore from the cache.
        if (File.Exists(Path.Combine(dir, keyword)))
            using (var reader = new StreamReader(File.OpenRead(Path.Combine(dir, keyword)), Encoding.UTF8))
                return reader.ReadToEnd();

        // Download dictionary page from Naver dictionary website.
        var url = "http://jpdic.naver.com/search_word.nhn?query={0}";
        var wc = new WebClient();
        var ret = Encoding.UTF8.GetString(
            wc.DownloadData(string.Format(url, HttpUtility.UrlEncode(keyword))));

        // Save to the cache.
        using (var writer = new StreamWriter(File.OpenWrite(Path.Combine(dir, keyword)), Encoding.UTF8))
            writer.Write(ret);

        return ret;
    }

    static IEnumerable<Word> Extract(string keyword)
    {
        var ret = GetPage(keyword);

        // Check whether the word correctly spelled.
        if (ret.Contains("검색결과가 없습니다."))
            throw new ApplicationException("Word not found: " + keyword);

        // Look for word search result.
        var i = ret.IndexOf("<dl class=\"entry_result\">");
        if (i == -1) yield break;

        // Just take out result listings.
        var j = ret.IndexOf("</dl>", i) + "</dl>".Length;
        ret = ret.Substring(i, j - i);

        // Image tags are not well formed in XML, so delete it. Additionally,
        // < and > in attribute values are treated as incorrect format by
        // XmlDocument class, so just remove by regular expressions.
        ret = ret.Replace("onmouseout=\"this.className = 'jpAutoLink';\" onmouseover=\"this.className = 'jpAutoLink pointbg';\" href=\"javascript:void(0);\"", "")
            .Replace("<span class='han' autolink>", "<span>")
            .Replace("&posIndex=", "");
        var regex = new Regex("<img [^>]+>");
        ret = regex.Replace(ret, m => "");
        regex = new Regex("param=\"[^\"]*\"");
        ret = regex.Replace(ret, m => "");

        // Parse it as an XML snippet.
        var doc = XElement.Parse(ret);
        foreach (var n in doc.Descendants().ToList())
            // Remove annotations of phonetic readings in Japanese.
            if (n.Name == "sup" && n.Attribute("class") != null &&
                n.Attribute("class").Value == "huri") n.Remove();

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

                // Check the entry is Korean word.
                var href = link.Attribute("href").Value;
                if (href.Contains("jpkr") || href.Contains("foreign") || href.Contains("hanja"))
                {
                    // If not, skip that entry.
                    current = null;
                    continue;
                }

                var id = int.Parse(href.Replace("/entry_krjp.nhn?entryId=", ""));
                var elem = link.Descendants("SUP").FirstOrDefault();
                var itemNumber = elem != null ? int.Parse(elem.Value) : null as int?;
                if (elem != null) elem.Remove();
                var text = link.Value;

                // If it can be written in Chinese letters, also take it.
                string hanja = null;
                var start = n.Nodes().FirstOrDefault(x => (hanja = x.ToString().Trim()).StartsWith("["));
                if (start != null)
                {
                    if (hanja.EndsWith("]")) hanja = hanja.Substring(1, hanja.Length - 2);
                    else
                    {
                        var node = start.NextNode;
                        hanja = hanja.Substring(1);
                        do
                        {
                            if (node is XElement)
                                hanja += (node as XElement).Value;
                            else
                            {
                                var hanjaPart = node.ToString().Trim();
                                if (hanjaPart.EndsWith("]"))
                                {
                                    hanja += hanjaPart.Substring(0, hanjaPart.Length - 1);
                                    break;
                                }
                                else hanja += hanjaPart;
                            }
                            node = node.NextNode;
                        } while (node.ToString().Trim() != "]");
                    }
                }
                else hanja = null;

                // Make a new instance of Word.
                current = new Word { ID = id, Reading = text, ItemNumber = itemNumber, Hanja = hanja };
            }
            else if (n.Name == "dd" && current != null)    // Word meaning.
            {
                string pos = null, desc = null;

                // The first element should be a bullet number like <strong>1</strong>.
                // In other cases, it can be <strong>『사람』</strong> for a person.
                // For the latter case, just skip.
                var elem = n.Elements().First();
                Debug.Assert(elem.Name == "strong");
                int id;
                if (!int.TryParse(elem.Value, out id)) continue;
                elem.Remove();

                // The second node should be a text, starting with a period.
                var node = n.Nodes().First();
                var text = node.ToString().Trim();
                if (text == ".")
                {
                    // Take POS annotation and remove it.
                    elem = n.Elements("em").Where(x => x.Attribute("class").Value == "pos").FirstOrDefault();
                    if (elem != null)
                    {
                        pos = elem.Value;
                        pos = pos.Substring(1, pos.Length - 2);
                        elem.Remove();
                    }
                }
                else
                {
                    // The text following a period is also a description.
                    text = text.Substring(1);
                    desc = text;
                }

                // All the rests are word's explanations.
                node.Remove();
                desc += n.Value.Trim();

                current.Meanings.Add(new Meaning { ID = id, POS = pos, Description = desc });
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
    public int? ItemNumber { get; set; }
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
    public int ID { get; set; }

    string pos = "";
    public string POS
    {
        get { return pos; }
        set
        {
            // Prepare conversion table.
            var table = new[,] {
                { "명사", "名詞"},      // Noun
                { "대명사", "代名詞" },   // Pronoun
                { "형용사", "形容詞" },   // Adjective
                { "자동사", "自動詞" },   // Intransitive verb
                { "타동사", "他動詞" },   // Transitive verb
                { "부사", "副詞" },     // Adverb
                { "관용구", "慣用句" },   // Common phrase

                { "관형사•명사", "冠形詞 / 名詞" },   // Demonstrative (?)
                { "명사·하다형 자동사", "名詞 / ハダ形 自動詞" },
                { "명사·하다형 타동사", "名詞 / ハダ形 他動詞" },
                { "명사·하다형 자·타동사", "名詞 / ハダ形 自動詞,他動詞" },
                { "명사·하다형 형용사", "名詞 / ハダ形 形容詞" },
                { "보조동사][여 불규칙활용", "補助動詞, 여 不規則活用" },
                { "보조형용사][여 불규칙활용", "補助形容詞, 여 不規則活用" },
                { "타동사·여 불규칙활용", "他動詞, 여 不規則活用" },
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
