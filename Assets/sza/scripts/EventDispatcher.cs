using System;
using System.Collections.Generic;
using UnityEngine;

public class EventDispatcher : MonoBehaviour
{
    private static EventDispatcher _instance;
    public static EventDispatcher Instance
    {
        get
        {
            if (_instance == null)
            {
                // 自动创建一个管理器对象
                var obj = new GameObject("EventDispatcher");
                _instance = obj.AddComponent<EventDispatcher>();
                DontDestroyOnLoad(obj);
            }
            return _instance;
        }
    }

    // 存储事件监听表：事件名 → 委托列表
    private Dictionary<string, Delegate> eventTable = new Dictionary<string, Delegate>();

    /// <summary>
    /// 注册事件监听
    /// </summary>
    public void AddListener<T>(string eventName, Action<T> listener)
    {
        if (eventTable.TryGetValue(eventName, out var del))
        {
            eventTable[eventName] = Delegate.Combine(del, listener);
        }
        else
        {
            eventTable[eventName] = listener;
        }
    }

    /// <summary>
    /// 移除事件监听
    /// </summary>
    public void RemoveListener<T>(string eventName, Action<T> listener)
    {
        if (eventTable.TryGetValue(eventName, out var del))
        {
            var currentDel = Delegate.Remove(del, listener);
            if (currentDel == null)
                eventTable.Remove(eventName);
            else
                eventTable[eventName] = currentDel;
        }
    }

    /// <summary>
    /// 派发事件
    /// </summary>
    public void Dispatch<T>(string eventName, T eventData)
    {
        if (eventTable.TryGetValue(eventName, out var del))
        {
            if (del is Action<T> callback)
                callback.Invoke(eventData);
            else
                Debug.LogWarning($"EventDispatcher: 类型不匹配 {eventName}");
        }
    }

    //无参数版本
    public void AddListener(string eventName, Action listener)
    {
        if (eventTable.TryGetValue(eventName, out var del))
            eventTable[eventName] = Delegate.Combine(del, listener);
        else
            eventTable[eventName] = listener;
    }

    public void RemoveListener(string eventName, Action listener)
    {
        if (eventTable.TryGetValue(eventName, out var del))
        {
            var currentDel = Delegate.Remove(del, listener);
            if (currentDel == null)
                eventTable.Remove(eventName);
            else
                eventTable[eventName] = currentDel;
        }
    }

    public void Dispatch(string eventName)
    {
        if (eventTable.TryGetValue(eventName, out var del))
        {
            if (del is Action callback)
                callback.Invoke();
        }
    }
}