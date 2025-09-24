using Godot;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MarrieGame.Scripts.Dialog
{
    /// <summary>
    /// Data structures for dialog system
    /// </summary>
    [System.Serializable]
    public class DialogData
    {
        [JsonPropertyName("dialogs")]
        public List<DialogEntry> Dialogs { get; set; } = new List<DialogEntry>();
        
        [JsonPropertyName("actions")]
        public Dictionary<string, string> Actions { get; set; } = new Dictionary<string, string>();
    }

    [System.Serializable]
    public class DialogEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }
        
        [JsonPropertyName("npcId")]
        public string NpcId { get; set; }
        
        [JsonPropertyName("text")]
        public string Text { get; set; }
        
        [JsonPropertyName("askContinue")]
        public bool AskContinue { get; set; }
        
        [JsonPropertyName("choices")]
        public List<DialogChoice> Choices { get; set; } = new List<DialogChoice>();
        
        [JsonPropertyName("questions")]
        public List<DialogQuestion> Questions { get; set; } = new List<DialogQuestion>();
        
        [JsonPropertyName("reward")]
        public DialogReward Reward { get; set; }
        
        [JsonPropertyName("action")]
        public string Action { get; set; }
    }

    [System.Serializable]
    public class DialogChoice
    {
        [JsonPropertyName("text")]
        public string Text { get; set; }
        
        [JsonPropertyName("nextDialog")]
        public string NextDialog { get; set; }
        
        [JsonPropertyName("action")]
        public string Action { get; set; }
        
        [JsonPropertyName("guardianStays")]
        public bool GuardianStays { get; set; }
        
        [JsonPropertyName("isCorrect")]
        public bool IsCorrect { get; set; }
    }

    [System.Serializable]
    public class DialogQuestion
    {
        [JsonPropertyName("text")]
        public string Text { get; set; }
        
        [JsonPropertyName("choices")]
        public List<DialogChoice> Choices { get; set; } = new List<DialogChoice>();
    }

    [System.Serializable]
    public class DialogReward
    {
        [JsonPropertyName("item")]
        public string Item { get; set; }
        
        [JsonPropertyName("quantity")]
        public int Quantity { get; set; }
    }
}