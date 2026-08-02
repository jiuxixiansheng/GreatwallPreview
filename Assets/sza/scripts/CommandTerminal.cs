using System.Collections;
using UnityEngine;

public class CommandTerminal : MonoBehaviour
{
    [Header("转场 01")]
    [SerializeField] private GameObject zhuanChang01HideObject;
    [SerializeField] private GameObject zhuanChang01ShowObject;
    [SerializeField] private float zhuanChang01SwitchDelay = 1.0f;

    [Header("转场 02")]
    [SerializeField] private GameObject zhuanChang02HideObject;
    [SerializeField] private GameObject zhuanChang02ShowObject;
    [SerializeField] private float zhuanChang02SwitchDelay = 1.0f;

    [Header("转场 03")]
    [SerializeField] private GameObject zhuanChang03HideObject;
    [SerializeField] private GameObject zhuanChang03ShowObject;
    [SerializeField] private float zhuanChang03SwitchDelay = 1.0f;

    public void ZhuanChang01()
    {
        EventDispatcher.Instance.Dispatch("zhuanchang");

        StartCoroutine(SwitchObjectAfterDelay(
            zhuanChang01HideObject,
            zhuanChang01ShowObject,
            zhuanChang01SwitchDelay
        ));
    }

    public void ZhuanChang02()
    {
        EventDispatcher.Instance.Dispatch("zhuanchang");

        StartCoroutine(SwitchObjectAfterDelay(
            zhuanChang02HideObject,
            zhuanChang02ShowObject,
            zhuanChang02SwitchDelay
        ));
    }

    public void ZhuanChang03()
    {
        EventDispatcher.Instance.Dispatch("zhuanchang");

        StartCoroutine(SwitchObjectAfterDelay(
            zhuanChang03HideObject,
            zhuanChang03ShowObject,
            zhuanChang03SwitchDelay
        ));
    }

    private IEnumerator SwitchObjectAfterDelay(GameObject hideObject, GameObject showObject, float delay)
    {
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        if (hideObject != null)
            hideObject.SetActive(false);

        if (showObject != null)
            showObject.SetActive(true);
    }
}