using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        var keyword = "졸업";
        var t = Extract(keyword).ToArray();
    }

    static IEnumerable<Word> Extract(string keyword)
    {
        var url = "http://jpdic.naver.com/search.nhn?query={0}";
        var wc = new WebClient();
        var ret = Encoding.UTF8.GetString(
            wc.DownloadData(string.Format(url, HttpUtility.UrlEncode(keyword))));

        var i = ret.IndexOf("<dl class=\"entry_result\">");
        if (i == -1) yield break;

        var j = ret.IndexOf("</dl>", i) + "</dl>".Length;
        ret = ret.Substring(i, j - i);

        var regex = new Regex("<img [^>]+>");
        ret = regex.Replace(ret, m => "");
        regex = new Regex("<span class=\"jpAutoLink\"[^\\n]+</span>\">");
        ret = regex.Replace(ret, m => "<span>");

        var doc = XElement.Parse(ret);
        foreach (var n in doc.Descendants().ToList())
            if (n.Name == "sup" && n.Attribute("class").Value == "huri") n.Remove();

        Word current = null;
        foreach (var n in doc.Elements())
        {
            if (n.Name == "dt")
            {
                if (current != null) yield return current;

                var link = n.Descendants().First();
                Debug.Assert(link.Name == "a");
                var id = int.Parse(link.Attribute("href").Value.Replace("/entry_krjp.nhn?entryId=", ""));
                var text = link.Value;

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

                current = new Word { ID = id, Reading = text, Hanja = hanja };
            }
            else if (n.Name == "dd")
            {
                n.Nodes().First().Remove();
                n.Nodes().First().Remove();

                var pos = n.Elements().First().Value;
                pos = pos.Substring(1, pos.Length - 2);
                n.Nodes().First().Remove();

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
            var table = new[,] {
                { "명사", "名詞"},
                { "대명사", "代名詞" },
                { "명사·하다형 자동사", "名詞 / 自動詞" },
                { "명사·하다형 타동사", "名詞 / 他動詞" },
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
