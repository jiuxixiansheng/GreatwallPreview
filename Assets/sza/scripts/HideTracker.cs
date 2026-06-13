using UnityEngine;

public class HideTracker : MonoBehaviour
{
    void OnDisable()
    {
        // 故意抛出一个警告（因为警告和报错会自动记录完整的代码调用堆栈）
        Debug.LogWarning("🚨 抓到你了！是谁把我隐藏的？请看下面完整的调用栈！", this);
    }
}