using TMPro;
using UnityEngine;

public class ParticipantConfig : MonoBehaviour
{
    public string participantId = "test_user";
    public string handedness = "unavailable";
    public TMP_InputField participantIdInput;
    [TextArea]
    public string notes = "";

    public string ApplyParticipantInput()
    {
        if (participantIdInput != null)
        {
            participantId = NormalizeParticipantId(participantIdInput.text);
            participantIdInput.text = participantId;
        }
        else
        {
            participantId = NormalizeParticipantId(participantId);
        }

        return participantId;
    }

    private string NormalizeParticipantId(string raw)
    {
        string value = string.IsNullOrWhiteSpace(raw) ? "P00" : raw.Trim();
        value = value.Replace(" ", "_");
        return value.Length > 24 ? value.Substring(0, 24) : value;
    }
}

