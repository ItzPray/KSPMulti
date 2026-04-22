using KSP.UI.Screens;
using LmpClient;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LmpClient.Harmony
{
    /// <summary>
    /// When a launch control is disabled because another player occupies the pad, hovering shows who (screen message).
    /// </summary>
    public class LaunchPadOccupiedHoverHint : MonoBehaviour, IPointerEnterHandler
    {
        public string SiteKey = string.Empty;
        public string OccupantName = string.Empty;

        private static string _lastMessageSite;
        private static float _lastMessageTime = -999f;

        private const float RepeatCooldownSeconds = 4f;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (string.IsNullOrEmpty(OccupantName))
                return;

            var now = Time.unscaledTime;
            if (now - _lastMessageTime < RepeatCooldownSeconds &&
                string.Equals(_lastMessageSite, SiteKey, System.StringComparison.Ordinal))
                return;

            _lastMessageSite = SiteKey ?? string.Empty;
            _lastMessageTime = now;

            LunaScreenMsg.PostScreenMessage(
                $"This launch site is in use by {OccupantName}.",
                5f,
                ScreenMessageStyle.UPPER_CENTER);
        }
    }
}
