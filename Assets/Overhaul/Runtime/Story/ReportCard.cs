using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// The ending (design doc: Reishi, Chaga and Maitake meet you outside the dome and give you your report card).
    /// Claims OverhaulHooks.FinalReport, so it plays right after the final-report dialogue and the flow waits for it.
    /// Rows stamp in one by one — Repair (Reishi), Dismantling (Chaga), Freedom (Maitake), Attunement, Curiosity —
    /// then the overall result and its ending: Excellent (an invitation to the intergalactic conference as their
    /// plus one), Moderate ("we'll text u") or Poor ("you failed, seeya"). Press E to continue.
    /// </summary>
    [OverhaulModule(72)]
    public class ReportCard : MonoBehaviour
    {
        Canvas _canvas;

        void OnEnable() => OverhaulHooks.FinalReport = Show;
        void OnDisable() { if (OverhaulHooks.FinalReport == Show) OverhaulHooks.FinalReport = null; }

        struct RowData { public string name, who, comment; public float score; public Color color; }

        IEnumerator Show()
        {
            var s = StoryStats.Instance;
            var grade = s != null ? s.Overall() : StoryStats.Grade.Moderate;
            string gradeId = grade == StoryStats.Grade.Excellent ? "excellent" : grade == StoryStats.Grade.Moderate ? "moderate" : "poor";
            GameSignals.RaiseReportCardShown(gradeId);

            var rows = BuildRows(s);
            _canvas = OverhaulUI.CreateCanvas("Report Card", 300, transform);
            var group = _canvas.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            var dim = OverhaulUI.Image("Dim", _canvas.transform, GameHUD.WhiteSprite, new Color(0.02f, 0.02f, 0.03f, 0.8f));
            OverhaulUI.Stretch(dim.rectTransform);
            var panel = OverhaulUI.Rect("Card", _canvas.transform);
            OverhaulUI.Place(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1180f, 900f));
            var plate = OverhaulUI.PanelImage("Plate", panel, new Color(0.05f, 0.06f, 0.08f, 0.96f), 20);
            OverhaulUI.Stretch(plate.rectTransform);

            var org = OverhaulUI.Label("Org", panel, "VYAL CIVIC DIPLOMACY PROGRAM", 15f, OverhaulUI.Accent, TextAlignmentOptions.Top, FontWeight.SemiBold, false, 5f);
            OverhaulUI.Place(org.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(1100f, 24f));
            var title = OverhaulUI.Label("Title", panel, "First Contact Field Report", 40f, OverhaulUI.Text, TextAlignmentOptions.Top, FontWeight.SemiBold);
            OverhaulUI.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -64f), new Vector2(1100f, 52f));
            var sub = OverhaulUI.Label("Sub", panel, "Subject: Escropita (human, En-Joy)   ·   Assessors: Reishi, Chaga, Maitake", 17f, OverhaulUI.Dim, TextAlignmentOptions.Top);
            OverhaulUI.Place(sub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -118f), new Vector2(1100f, 26f));

            // fade in
            for (float t = 0f; t < 0.6f; t += Time.unscaledDeltaTime) { group.alpha = t / 0.6f; yield return null; }
            group.alpha = 1f;

            float y = -170f;
            foreach (var r in rows)
            {
                yield return Row(panel, r, y);
                y -= 104f;
            }

            // overall
            string stampText, ending;
            Color stampColor;
            Ending(grade, out stampText, out stampColor, out ending);
            var summary = OverhaulUI.Label("Summary", panel, Summary(s), 16f, OverhaulUI.Dim, TextAlignmentOptions.Top, FontWeight.Regular, true);
            OverhaulUI.Place(summary.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y - 6f), new Vector2(1080f, 26f));
            var stamp = OverhaulUI.Label("Stamp", panel, stampText, 46f, stampColor, TextAlignmentOptions.Center, FontWeight.Bold, false, 6f);
            OverhaulUI.Place(stamp.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, y - 70f), new Vector2(700f, 70f));
            var end = OverhaulUI.Label("Ending", panel, ending, 19f, OverhaulUI.Text, TextAlignmentOptions.Top, FontWeight.Regular, true);
            OverhaulUI.Place(end.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y - 118f), new Vector2(1000f, 120f));
            Sfx.Play("ui.checkpoint", null, 1f);
            for (float t = 0f; t < 0.45f; t += Time.unscaledDeltaTime)
            {
                float k = t / 0.45f;
                stamp.rectTransform.localScale = Vector3.one * Mathf.Lerp(2.2f, 1f, 1f - (1f - k) * (1f - k));
                stamp.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-14f, -4f, k));
                stamp.alpha = k;
                end.alpha = k;
                yield return null;
            }

            string accent = OverhaulUI.Hex(OverhaulUI.Accent);
            var hint = OverhaulUI.Label("Hint", panel, $"Press <color=#{accent}><b>E</b></color> to continue", 16f, OverhaulUI.Dim, TextAlignmentOptions.Center);
            OverhaulUI.Place(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(600f, 24f));

            float shownAt = Time.unscaledTime;
            while (true)
            {
                if (Time.unscaledTime - shownAt > 0.6f &&
                    (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0)))
                    break;
                hint.alpha = 0.55f + 0.45f * Mathf.Sin((Time.unscaledTime - shownAt) * 3f);
                yield return null;
            }
            Sfx.Play("ui.close", null, 0.8f);
            for (float t = 0f; t < 0.5f; t += Time.unscaledDeltaTime) { group.alpha = 1f - t / 0.5f; yield return null; }
            Destroy(_canvas.gameObject);
        }

        IEnumerator Row(RectTransform panel, RowData r, float y)
        {
            var row = OverhaulUI.Rect("Row " + r.name, panel);
            OverhaulUI.Place(row, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(1080f, 92f));
            var cg = row.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            var plate = OverhaulUI.PanelImage("Plate", row, new Color(1f, 1f, 1f, 0.04f), 12);
            OverhaulUI.Stretch(plate.rectTransform);
            var dot = OverhaulUI.Image("Dot", row, OverhaulUI.SoftCircle, r.color);
            OverhaulUI.Place(dot.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(30f, 14f), new Vector2(22f, 22f));
            var name = OverhaulUI.Label("Name", row, r.name, 22f, OverhaulUI.Text, TextAlignmentOptions.MidlineLeft, FontWeight.SemiBold);
            OverhaulUI.Place(name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, -10f), new Vector2(260f, 30f));
            var barBack = OverhaulUI.Image("BarBack", row, GameHUD.RoundedSprite(4), new Color(1f, 1f, 1f, 0.1f), Image.Type.Sliced);
            OverhaulUI.Place(barBack.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(320f, -20f), new Vector2(560f, 10f));
            var bar = OverhaulUI.Image("Bar", barBack.rectTransform, GameHUD.RoundedSprite(4), r.color, Image.Type.Sliced);
            bar.rectTransform.anchorMin = new Vector2(0f, 0f);
            bar.rectTransform.anchorMax = new Vector2(0f, 1f);
            bar.rectTransform.pivot = new Vector2(0f, 0.5f);
            bar.rectTransform.anchoredPosition = Vector2.zero;
            bar.rectTransform.sizeDelta = new Vector2(0f, 0f);
            var comment = OverhaulUI.Label("Comment", row, $"<b>{r.who}:</b> {r.comment}", 16f, OverhaulUI.Dim, TextAlignmentOptions.TopLeft, FontWeight.Regular, true);
            OverhaulUI.Place(comment.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, -44f), new Vector2(880f, 44f));
            var letter = OverhaulUI.Label("Grade", row, StoryStats.Letter(r.score), 52f, r.color, TextAlignmentOptions.Center, FontWeight.Bold);
            OverhaulUI.Place(letter.rectTransform, new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-70f, 0f), new Vector2(110f, 80f));
            letter.alpha = 0f;

            for (float t = 0f; t < 0.3f; t += Time.unscaledDeltaTime)
            {
                cg.alpha = t / 0.3f;
                row.anchoredPosition = new Vector2(Mathf.Lerp(-30f, 0f, t / 0.3f), y);
                yield return null;
            }
            cg.alpha = 1f;
            row.anchoredPosition = new Vector2(0f, y);
            for (float t = 0f; t < 0.5f; t += Time.unscaledDeltaTime)
            {
                float k = 1f - (1f - t / 0.5f) * (1f - t / 0.5f);
                bar.rectTransform.sizeDelta = new Vector2(560f * r.score * k, 0f);
                yield return null;
            }
            bar.rectTransform.sizeDelta = new Vector2(560f * r.score, 0f);
            Sfx.Play("ui.click", null, 1f, 0.9f + 0.2f * r.score);
            for (float t = 0f; t < 0.25f; t += Time.unscaledDeltaTime)
            {
                float k = t / 0.25f;
                letter.alpha = k;
                letter.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.9f, 1f, k);
                letter.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-18f, -6f, k));
                yield return null;
            }
            letter.alpha = 1f;
            letter.rectTransform.localScale = Vector3.one;
            yield return WaitUnscaled(0.12f);
        }

        static IEnumerator WaitUnscaled(float s)
        {
            for (float t = 0f; t < s; t += Time.unscaledDeltaTime) yield return null;
        }

        static RowData[] BuildRows(StoryStats s)
        {
            int rep = s != null ? s.Repairs : 0, dis = s != null ? s.Dismantles : 0, fre = s != null ? s.Freedom : 0;
            int solved = s != null ? s.PuzzlesSolved : 0, failed = s != null ? s.PuzzlesFailed : 0, att = s != null ? s.Attuned : 0;
            int lore = s != null ? s.LoreRead.Count : 0, scans = s != null ? s.Scans : 0;
            float attune = Mathf.Clamp01(solved * 0.22f + att * 0.18f);
            float curiosity = Mathf.Clamp01(lore / 6f * 0.75f + Mathf.Min(scans, 10) / 10f * 0.25f);
            return new[]
            {
                new RowData { name = "Repair", who = "Reishi", color = OverhaulPalette.RoleFixer, score = StoryStats.Score(rep),
                              comment = Pick(StoryStats.Score(rep),
                                  "Textbook reciprocity! I'm putting this in the class newsletter.",
                                  "Solid repairs. With a little more initiative you'd be class-president material. (I am class president.)",
                                  "You fixed things… occasionally. The windmill noticed.",
                                  "The windmill filed a complaint. I co-signed it.") + $"  ({rep}/{StoryStats.TaskGoal})" },
                new RowData { name = "Dismantling", who = "Chaga", color = OverhaulPalette.RoleBreaker, score = StoryStats.Score(dis),
                              comment = Pick(StoryStats.Score(dis),
                                  "You smashed exactly the right stuff. Best. Field trip. Ever.",
                                  "Good wrecking. Could've wrecked more. I'm just saying.",
                                  "Barely any rubble. Did you even want to be here?",
                                  "Zero things destroyed. Boring. Is that a human thing?") + $"  ({dis}/{StoryStats.TaskGoal})" },
                new RowData { name = "Freedom", who = "Maitake", color = OverhaulPalette.RoleWanderer, score = StoryStats.Score(fre),
                              comment = Pick(StoryStats.Score(fre),
                                  "…you walked us all the way to the door. Thank you. (I waved. You didn't see.)",
                                  "Some of us got to wander home. That was nice.",
                                  "I waited by the door for a while. It's okay.",
                                  "I'm still out here. That's fine. I like the wind.") + $"  ({fre}/{StoryStats.TaskGoal})" },
                new RowData { name = "Attunement", who = "The class", color = OverhaulPalette.SporeCyan, score = attune,
                              comment = $"{solved} songs answered, {failed} misses, {att} Mycari perceived without a scanner. Curiosity counts more than correctness." },
                new RowData { name = "Curiosity", who = "Daffodil (unsolicited)", color = OverhaulPalette.HudAccent, score = curiosity,
                              comment = $"You read {lore} of {LoreLibrary.Ids.Length} logs and scanned {scans} times. I recorded all of it. With permission, this time." },
            };
        }

        static string Pick(float score, string a, string b, string c, string d)
        {
            string l = StoryStats.Letter(score);
            return l == "A" ? a : l == "B" ? b : l == "C" ? c : d;
        }

        static string Summary(StoryStats s)
        {
            if (s == null) return "";
            int mins = Mathf.FloorToInt(s.PlayTime / 60f);
            return $"Time on Mars {mins} min   ·   Lowest life support {Mathf.RoundToInt(s.LowestLife * 100f)}%   ·   Shards gathered {s.Shards}   ·   Rollbacks {s.Rollbacks}";
        }

        static void Ending(StoryStats.Grade g, out string stamp, out Color color, out string text)
        {
            switch (g)
            {
                case StoryStats.Grade.Excellent:
                    stamp = "EXCELLENT";
                    color = OverhaulPalette.Good;
                    text = "“You did decently on the tests. We were curious, after previous human failures, if you'd bother to listen. You surprised us, but not that much :)”\n" +
                           "You are formally invited to the Intergalactic Conference on Shared Flourishing — as their plus one. Symposiums! Presentations! Snacks of unclear origin!";
                    break;
                case StoryStats.Grade.Moderate:
                    stamp = "MODERATE";
                    color = OverhaulPalette.Warn;
                    text = "“That was a good date… we'll text u.”\nThey couldn't quite get a vibe check. There was, however, a vibe.";
                    break;
                default:
                    stamp = "POOR";
                    color = OverhaulPalette.Bad;
                    text = "“I mean, we were testing you, you failed, seeya.”\nEmbarrassing your entire species, and they know about it. (Attached: one grumpy-cat meme. Their memes are a few decades behind.)";
                    break;
            }
        }
    }
}
