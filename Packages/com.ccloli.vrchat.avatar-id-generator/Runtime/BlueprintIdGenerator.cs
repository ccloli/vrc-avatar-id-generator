using UnityEngine;
using UnityEditor;
using UnityEngine.Networking;
using System.Text;
using System;
using System.Collections.Generic;
using System.Reflection;

// 尝试引用 VRC SDK 的命名空间，如果报错请确保项目中有 VRC SDK
// 如果因版本太老找不到命名空间，后续代码使用了反射作为保底
using VRC.Core;

public class BlueprintIDGenerator : EditorWindow
{
    // --- UI 状态变量 ---
    private string authCookie = "";
    private string userAgent = "BlueprintIDGenerator/0.1.0"; // 默认值，会被 SDK 覆盖
    private string avatarName = "";
    private string generatedID = "";
    private string logMessage = "";
    private bool useSDKSession = true;

    // --- 目标对象 ---
    private GameObject targetAvatar;
    private Component pipelineManager; // 动态存储，兼容不同版本

    // --- API 常量 ---
    private const string API_URL = "https://api.vrchat.cloud/api/1/avatars";

    [MenuItem("Tools/Avatar ID Generator")]
    public static void ShowWindow()
    {
        GetWindow<BlueprintIDGenerator>("Avatar ID Gen");
    }

    private void OnEnable()
    {
        // 窗口打开时，尝试自动获取 User-Agent
        FetchUserAgentFromSDK();
        // 尝试自动获取 Cookie
        if (useSDKSession) TryFetchSessionFromSDK();
    }

    void OnGUI()
    {
        GUILayout.Label("VRChat Avatar Blueprint ID 生成器", EditorStyles.boldLabel);
        EditorGUIUtility.labelWidth = 120;
        EditorGUILayout.HelpBox(
            "此工具适用于低版本 SDK (< 3.9.0) 新增 avatar 时，服务端提示 \"Id is not allowed to be used., Make sure you're using SDK 3.9.0 or newer.\" 而被禁止上传的问题。\n" +
            "此工具会参照新版 SDK 的流程，调用服务端 API 新增 avatar 并生成合法的 Blueprint ID (avtr_*)，填写到 avatar 的 Pipeline Manager 上，从而在旧版本 SDK 上也能上传新 avatar。\n" + 
            "使用新版本 SDK (>= 3.9.0) 或在旧版 SDK 上更新已上传过的 avatar 则不需要使用此工具。",
            MessageType.Info
           );

        // ---------------------------------------------------------
        // 1. Avatar 绑定区域
        // ---------------------------------------------------------
        GUILayout.Space(10);
        GUILayout.Label("1. 目标 Avatar设置", EditorStyles.boldLabel);

        GameObject newTarget = (GameObject)EditorGUILayout.ObjectField("目标物体", targetAvatar, typeof(GameObject), true);

        // 当用户改变选择时进行逻辑判断
        if (newTarget != targetAvatar)
        {
            targetAvatar = newTarget;
            AnalyzeTargetAvatar();
        }

        if (targetAvatar != null)
        {
            if (pipelineManager == null)
            {
                EditorGUILayout.HelpBox("该物体没有 Pipeline Manager 组件！", MessageType.Warning);
                if (GUILayout.Button("添加 Pipeline Manager"))
                {
                    // 尝试动态添加组件（防止旧版SDK类名差异，通常是 VRC.Core.PipelineManager）
                    // 这里为了稳健使用字符串查找类型，但通常您可以直接 AddComponent<PipelineManager>()
                    System.Type pmType = Type.GetType("VRC.Core.PipelineManager, VRCCore-Editor") ?? Type.GetType("VRC.Core.PipelineManager, Assembly-CSharp");
                    if (pmType != null) targetAvatar.AddComponent(pmType);
                    AnalyzeTargetAvatar();
                }
            }
            else
            {
                // 读取当前的 Blueprint ID
                SerializedObject so = new SerializedObject(pipelineManager);
                SerializedProperty bpProp = so.FindProperty("blueprintId");
                string currentId = bpProp.stringValue;

                if (!string.IsNullOrEmpty(currentId))
                {
                    EditorGUILayout.HelpBox($"警告: 当前已绑定 ID: {currentId}", MessageType.Error);
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Detach (清除 ID)"))
                    {
                        bpProp.stringValue = "";
                        so.ApplyModifiedProperties();
                        generatedID = ""; // 重置生成器显示
                        // 重新分析以刷新名称
                        AnalyzeTargetAvatar();
                        Repaint();
                    }
                    GUILayout.EndHorizontal();
                }
                else
                {
                    EditorGUILayout.HelpBox("状态就绪: 可以生成并写入新 ID。", MessageType.Info);
                    // 如果名字是空的，强制同步一次
                    if (string.IsNullOrEmpty(avatarName)) avatarName = targetAvatar.name;
                }
            }
        }

        // ---------------------------------------------------------
        // 2. 参数设置
        // ---------------------------------------------------------
        GUILayout.Space(10);
        GUILayout.Label("2. API 参数", EditorStyles.boldLabel);

        avatarName = EditorGUILayout.TextField("Avatar 名称", avatarName);

        GUILayout.Space(5);

        //// User Agent 显示
        EditorGUILayout.LabelField("User-Agent", EditorStyles.miniLabel);
        GUI.enabled = false; // 只读
        EditorGUILayout.TextField(userAgent);
        GUI.enabled = true;

        // ---------------------------------------------------------
        // 3. 认证设置
        // ---------------------------------------------------------
        GUILayout.Space(10);
        GUILayout.Label("3. 认证方式", EditorStyles.boldLabel);

        useSDKSession = EditorGUILayout.Toggle("尝试复用 SDK 登录", useSDKSession);

        if (!useSDKSession)
        {
            EditorGUILayout.HelpBox("手动模式: 请从浏览器 F12 -> Application -> Cookies 复制 'auth'。", MessageType.None);
            authCookie = EditorGUILayout.TextField("Auth Cookie", authCookie);
            userAgent = "BlueprintIDGenerator/0.1.0"; // 400 Using a web-derived token in client
        }
        else
        {
            if (string.IsNullOrEmpty(authCookie))
            {
                EditorGUILayout.HelpBox("尝试从 VRChat SDK 读取 Session 中...\n若一直显示此消息，请手动切换至 VRChat SDK 面板，然后点击手动刷新", MessageType.Info);
                if (GUILayout.Button("手动刷新 Session 读取"))
                {
                    TryFetchSessionFromSDK();
                }
            }
            else
            {
                EditorGUILayout.HelpBox("已成功读取 SDK Session!", MessageType.Info);
            }
            userAgent = "VRC.Core.BestHTTP"; // 400 Using a client-derived token outside of client
        }

        // ---------------------------------------------------------
        // 4. 执行按钮
        // ---------------------------------------------------------
        GUILayout.Space(15);

        GUI.enabled = !string.IsNullOrEmpty(avatarName); // 没名字不让点
        if (GUILayout.Button("生成并自动绑定", GUILayout.Height(30)))
        {
            if (string.IsNullOrEmpty(authCookie))
            {
                logMessage = "错误: 未获取到 Auth Cookie。请先在 SDK 控制面板登录，或手动输入 Cookie。";
            }
            else
            {
                GenerateID();
            }
        }
        GUI.enabled = true;

        // ---------------------------------------------------------
        // 5. 日志区
        // ---------------------------------------------------------
        if (!string.IsNullOrEmpty(logMessage))
        {
            GUILayout.Space(10);
            EditorGUILayout.HelpBox(logMessage, logMessage.StartsWith("错误") ? MessageType.Error : MessageType.Info);
        }

        if (!string.IsNullOrEmpty(generatedID))
        {
            GUILayout.Space(5);

            GUILayout.Label("生成的 ID", EditorStyles.boldLabel);
            EditorGUILayout.TextField(generatedID);
            if (GUILayout.Button("复制 ID"))
            {
                EditorGUIUtility.systemCopyBuffer = generatedID;
                logMessage = "已复制到剪贴板！请粘贴到 Pipeline Manager。";
            }
        }
    }

    // --- 核心逻辑方法 ---

    void AnalyzeTargetAvatar()
    {
        if (targetAvatar == null) return;

        // 尝试获取 PipelineManager
        pipelineManager = targetAvatar.GetComponent("PipelineManager");

        // 如果没有 ID，且名字还是默认值，则自动填入物体名
        if (pipelineManager != null)
        {
            SerializedObject so = new SerializedObject(pipelineManager);
            string bid = so.FindProperty("blueprintId").stringValue;
            if (string.IsNullOrEmpty(bid))
            {
                avatarName = targetAvatar.name;
            }
        }
    }

    // FIXME: invalided
    void FetchUserAgentFromSDK()
    {
        try
        {
            // 尝试通过反射获取 VRC.Core.Tools.DeviceUserAgent 或 VRC.Core.API.DeviceUserAgent
            // 优先尝试 Tools 类 (常见于旧版 SDK)
            Type toolsType = Type.GetType("VRC.Core.Tools, VRCCore-Editor") ?? Type.GetType("VRC.Core.Tools, Assembly-CSharp");
            if (toolsType != null)
            {
                PropertyInfo prop = toolsType.GetProperty("DeviceUserAgent", BindingFlags.Static | BindingFlags.Public);
                if (prop != null)
                {
                    userAgent = (string)prop.GetValue(null, null);
                    return;
                }
            }

            // 再次尝试 API 类
            Type apiType = Type.GetType("VRC.Core.API, VRCCore-Editor");
            if (apiType != null)
            {
                PropertyInfo prop = apiType.GetProperty("DeviceUserAgent", BindingFlags.Static | BindingFlags.Public);
                if (prop != null) userAgent = (string)prop.GetValue(null, null);
            }
        }
        catch { /* 忽略错误，使用默认值 */ }
    }

    void TryFetchSessionFromSDK()
    {
        authCookie = "";
        try
        {
            // 方法 A: 通过 API.GetHeaders() (较新的 SDK)
            // 需要反射调用 internal 或 public static 方法
            Type apiType = Type.GetType("VRC.Core.API, VRCCore-Editor") ?? Type.GetType("VRC.Core.API, Assembly-CSharp");
            if (apiType != null)
            {
                // FIXME: conflict
                //// 检查是否登录(CurrentUser != null)
                //PropertyInfo currentUserProp = apiType.GetProperty("CurrentUser", BindingFlags.Static | BindingFlags.Public);
                //object user = currentUserProp?.GetValue(null, null);

                //if (user == null)
                //{
                //    logMessage = "SDK 未登录。请先打开 VRChat SDK Control Panel 并登录。";
                //    return;
                //}

                // 尝试获取 API Key (虽然我们硬编码了，但检查一下 SDK 状态)
                // 难点：Cookie 通常隐藏在 HTTP Client 内部。
                // 但是，我们可以尝试查找 API.Credentials (旧版)

                // 策略：检查 Unity 的 EditorPrefs，因为 VRChat SDK 经常把 Auth Token 存在那里
                // 常见的 Key: "vrchat_sdk_auth" (这是 Base64 编码的 user:pass 或 token)
                // 或者 "VRC_AUTH"

                // 如果我们找不到直接的 Cookie 字符串，我们至少可以确认用户已登录。
                // *Hack*: 由于无法轻易从 C# HTTP Client 提取 HttpOnly Cookie，
                // 如果 SDK 自身能工作，最好的办法其实是使用 API.SendRequest。
                // 但既然用户说 SDK 坏了，我们只能尝试找 EditorPrefs 里的 "auth" 字段。

                // 在非常旧的 SDK 中，ApiCredentials.authToken 存的是 token
                Type credType = Type.GetType("VRC.Core.ApiCredentials, VRCCore-Editor");
                if (credType != null)
                {
                    FieldInfo tokenField = credType.GetField("authToken", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    string token = (string)tokenField?.GetValue(null);
                    FieldInfo twoFactorAuthTokenField = credType.GetField("twoFactorAuthToken", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    string twoFactorAuthToken = (string)twoFactorAuthTokenField?.GetValue(null);
                    if (!string.IsNullOrEmpty(token))
                    {
                        // 如果这是一个 auth token，我们可以构造 cookie
                        authCookie = "auth=" + token; // 注意：格式可能需要调整，视 SDK 版本而定
                        if (!string.IsNullOrEmpty(twoFactorAuthToken))
                        {
                            authCookie += "; twoFactorAuth=" + twoFactorAuthToken;
                        }
                        logMessage = "已从 ApiCredentials 获取 Token。"; // + authCookie;
                        //foreach (FieldInfo field in credType.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                        //{
                        //    Console.WriteLine($"字段名: {field.Name}, 类型: {field.FieldType.Name}");

                        //    logMessage += $"\n属性名: {field.Name}, {credType.GetField(field.Name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)}";
                        //}
                        return;
                    }
                }
            }
        }
        catch (Exception e)
        {
            logMessage = "尝试读取 SDK Session 失败: " + e.Message;
        }
    }

    void GenerateID()
    {
        logMessage = "正在请求服务器...";

        string jsonPayload = "{" +
            "\"name\": \"" + avatarName + "\"," +
            //"\"imageUrl\": \"\"," +
            "\"releaseStatus\": \"private\"," +
            "\"unityVersion\": \"" + Application.unityVersion + "\"" + // 418 Unity version 2018.1.1a1 too old
            "}";

        UnityWebRequest request = new UnityWebRequest(API_URL, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();

        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("User-Agent", userAgent); // 使用获取到的 SDK UA

        // 处理 Cookie：如果 authCookie 不包含 "auth=" 前缀，简单处理一下
        string cookieHeader = authCookie;
        if (!cookieHeader.Contains("auth=") && !cookieHeader.Contains(";"))
        {
            // 假设用户只填了 token 值
            cookieHeader = "auth=" + authCookie;
        }
        request.SetRequestHeader("Cookie", cookieHeader);

        var operation = request.SendWebRequest();
        operation.completed += (op) =>
        {
            if (request.responseCode == 200)
            {
                try
                {
                    string response = request.downloadHandler.text;
                    // 简单的 JSON 解析
                    int idIndex = response.IndexOf("\"id\":\"");
                    if (idIndex != -1)
                    {
                        int start = idIndex + 6;
                        int end = response.IndexOf("\"", start);
                        generatedID = response.Substring(start, end - start);

                        logMessage = "成功！ID 已生成: " + generatedID;

                        // --- 自动设置逻辑 ---
                        if (targetAvatar != null && pipelineManager != null)
                        {
                            Undo.RecordObject(pipelineManager, "Set Blueprint ID");
                            SerializedObject so = new SerializedObject(pipelineManager);
                            so.FindProperty("blueprintId").stringValue = generatedID;
                            so.ApplyModifiedProperties();

                            // 标记为脏，确保保存
                            EditorUtility.SetDirty(pipelineManager);
                            logMessage += "\n已自动写入到 Pipeline Manager！";
                        }
                    }
                    else
                    {
                        logMessage = "解析错误: 返回数据异常。" + response;
                    }
                }
                catch (Exception e)
                {
                    logMessage = "异常: " + e.Message;
                }
            }
            else
            {
                logMessage = $"API 请求失败 [{request.responseCode}]。\n原因: {request.error}\n{request.downloadHandler.text }\n如果是 401，说明 SDK Session 已失效或 Cookie 错误。";
            }
            request.Dispose();
            Repaint();
        };
    }
}