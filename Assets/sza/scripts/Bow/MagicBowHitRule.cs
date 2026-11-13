using UnityEngine;

namespace Sza.Bow
{
    /// <summary>Optional override for an object and its descendants; automatic detection needs no component.</summary>
    public sealed class MagicBowHitRule : MonoBehaviour
    {
        public enum Response { Automatic, Include, Ignore }
        [Tooltip("Include 也允许无可见模型的 Trigger 被箭命中；Ignore 排除这个物体及其子物体。")]
        public Response response;

        public static Response Resolve(Transform target)
        {
            for (var t = target; t != null; t = t.parent)
            {
                var rule = t.GetComponent<MagicBowHitRule>();
                if (rule != null && rule.enabled && rule.response != Response.Automatic) return rule.response;
            }
            return Response.Automatic;
        }
    }
}
