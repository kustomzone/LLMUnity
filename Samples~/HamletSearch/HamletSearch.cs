using System.Collections.Generic;
using UnityEngine;
using System.Diagnostics;
using Debug = UnityEngine.Debug;
using System.Text.RegularExpressions;
using LLMUnity;

class HamletSearch : MonoBehaviour
{
    EmbeddingModel embedder;
    public bool fullPlay;
    public TextAsset gutenbergText;

    public void OnEnable()
    {
        embedder = new BGEModel(
            "Assets/StreamingAssets/bge-small-en-v1.5.sentis",
            "Assets/StreamingAssets/bge-small-en-v1.5.tokenizer.json"
        );
        // embedder = new BGEModel(
        //     "Assets/StreamingAssets/bge-base-en-v1.5.sentis",
        //     "Assets/StreamingAssets/bge-base-en-v1.5.tokenizer.json"
        // );
    }

    void Start()
    {
        Dictionary<string, List<(string, string)>> hamlet = ReadGutenbergFile(gutenbergText.text);
        DialogueManager dialogueManager = new DialogueManager(embedder);
        Stopwatch stopwatch = new Stopwatch();

        float elapsedTotal = 0;
        foreach ((string act, List<(string, string)> messages) in hamlet)
        {
            if (!fullPlay && act != "ACT III") continue;
            stopwatch.Reset(); stopwatch.Start();
            foreach ((string actor, string message) in messages)
                dialogueManager.Add(actor, act, message);

            elapsedTotal += (float)stopwatch.Elapsed.TotalMilliseconds / 1000f;
            Debug.Log($"act {act} embedded {dialogueManager.GetSentences(null, act).Count} sentences in {stopwatch.Elapsed.TotalMilliseconds / 1000f} secs");
        }
        Debug.Log($"embedded {dialogueManager.NumPhrases()} phrases, {dialogueManager.NumSentences()} sentences in {elapsedTotal} secs");

        stopwatch.Reset(); stopwatch.Start();
        string[] similar = dialogueManager.Search("should i be?", 10);
        stopwatch.Stop();
        Debug.Log($"search time: {stopwatch.Elapsed.TotalMilliseconds / 1000f} secs");

        Debug.Log("Similar sentences:");
        for (int i = 0; i < similar.Length; i++)
        {
            Debug.Log($"  {i + 1}: {similar[i]}");
        }
    }

    public Dictionary<string, List<(string, string)>> ReadGutenbergFile(string text)
    {
        string skipPattern = @"\[.*?\]";
        string namePattern = "^[A-Z and]+\\.$";
        Regex nameRegex = new Regex(namePattern);

        string act = null;
        string name = null;
        string message = "";
        bool add = false;
        Dialogue dialogue = null;
        int numWords = 0;
        int numLines = 0;
        Dictionary<string, List<(string, string)>> messages = new Dictionary<string, List<(string, string)>>();

        string[] lines = text.Split("\n");
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            if (line.Contains("***")) add = !add;
            if (!add) continue;

            line = line.Replace("\r", "");
            line = Regex.Replace(line, skipPattern, "");
            if (line == "") continue;
            numWords += line.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries).Length;
            numLines++;

            if (line.StartsWith("ACT"))
            {
                if (dialogue != null && message != "")
                {
                    messages[act].Add((name, message));
                }
                act = line.Replace(".", "");
                messages[act] = new List<(string, string)>();
                name = null;
                message = "";
            }
            else if (nameRegex.IsMatch(line))
            {
                if (name != null && message != "")
                {
                    messages[act].Add((name, message));
                }
                message = "";
                name = line.Replace(".", "");
            }
            else if (name != null)
            {
                if (message != "") message += " ";
                message += line;
            }
        }
        Debug.Log($"{numLines} lines, {numWords} words");
        return messages;
    }

    public void OnDisable()
    {
        embedder.Destroy();
    }
}