using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

using static TodoCore;
using Random = UnityEngine.Random;

/// <summary>Used to load and parse "TODO" tasks from scripts and .todo files</summary>
static class TodoCore
{
	public static GUIStyle boldCenteredStyle;
	public static bool hideInspectorToDos
	{
		get => PlayerPrefs.GetInt(nameof(hideInspectorToDos), 0) == 1;
		set => PlayerPrefs.SetInt(nameof(hideInspectorToDos), value ? 1 : 0);
	}

	static ExcludedList _excludedScripts;
	public static ExcludedList excludedScripts
	{
		get
		{
			if (_excludedScripts == null)
			{
				if (PlayerPrefs.HasKey(nameof(excludedScripts)))
				{
					_excludedScripts = JsonUtility.FromJson<ExcludedList>(
						PlayerPrefs.GetString(nameof(excludedScripts))
					);
				}
				else
					_excludedScripts = new ExcludedList();
			}

			return _excludedScripts;
		}
	}

	public static bool needsReload => scriptTodos == null || textTodos == null;

	static List<Todo> scriptTodos;
	static List<TextTodo> textTodos;
	static GUIStyle frameStyle;

	public static void Init()
	{
		if (boldCenteredStyle == null)
		{
			boldCenteredStyle = new GUIStyle(GUI.skin.label)
			{
				alignment = TextAnchor.MiddleCenter,
				fontStyle = FontStyle.Bold
			};
		}

		if (frameStyle == null)
		{
			frameStyle = new GUIStyle(GUI.skin.label)
			{
				wordWrap = true,
				richText = true
			};
		}

		if (needsReload)
			LoadAllToDos();
	}

	public static List<Todo> GetScriptTodos() => scriptTodos;

	public static List<TextTodo> GetFileTodos() => textTodos;

	public static List<Todo> GetComponentTodos(Type type) => scriptTodos.FindAll(item => item.fileName == type.Name);

	public static void LoadAllToDos()
	{
		string[] assetsPaths = AssetDatabase.GetAllAssetPaths();
		scriptTodos = new List<Todo>();
		textTodos = new List<TextTodo>();

		foreach (string assetPath in assetsPaths)
		{
			if (!assetPath.Contains("Packages") && !assetPath.Contains(typeof(TodoList).ToString()))
			{
				string[] lines;

				if (assetPath.EndsWith(".cs"))
				{
					TextAsset script = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);

					if (!excludedScripts.scriptsNames.Contains(script.name) && ContainsTodo(script.text, true))
					{
						lines = script.text.Split(
							new char[] { '\n' },
							System.StringSplitOptions.RemoveEmptyEntries
						);

						for (int i = 0; i < lines.Length; i++)
						{
							if (ContainsTodo(lines[i], true))
							{
								string[] words = lines[i].Split(
									new string[] { "TODO", ":" },
									System.StringSplitOptions.RemoveEmptyEntries
								);

								if (words[1] == " ")
									scriptTodos.Add(new Todo(script, words[2].TrimStart(' '), i + 1));
								else
									scriptTodos.Add(new Todo(script, words[1].Trim(' '), i + 1));
							}
						}
					}
				}

				if (assetPath.EndsWith(".todo"))
				{
					TextAsset textFile = new TextAsset(File.ReadAllText(assetPath));

					string[] frags = assetPath.Split('/');
					textFile.name = frags[frags.Length - 1];

					if (ContainsTodo(textFile.text, false))
					{
						lines = textFile.text.Split(
							new char[] { '\n' },
							System.StringSplitOptions.RemoveEmptyEntries
						);

						for (int i = 0; i < lines.Length; i++)
						{
							if (lines[i][0] == '-')
								textTodos.Add(new TextTodo(textFile, lines[i]));
						}
					}
				}
			}
		}
	}

	public static void DisplayTodo(TextTodo todo, bool isScript)
	{
		if (isScript)
		{
			if (GUILayout.Button("- " + todo.text, frameStyle, TodoList.GetMaxWidth()))
				(todo as Todo).Open();
		}
		else
			EditorGUILayout.TextArea(todo.text, frameStyle, TodoList.GetMaxWidth());
	}

	public static void SaveExcludes()
	{
		PlayerPrefs.SetString(nameof(excludedScripts), JsonUtility.ToJson(excludedScripts));
	}

	public static void RemoveTodosForScript(string fileName)
	{
		List<Todo> toRemove = scriptTodos.FindAll(item => item.fileName == fileName);
		toRemove.ForEach(item => scriptTodos.Remove(item));
	}

	static bool ContainsTodo(string text, bool isScript)
	{
		if (isScript)
			return text.Replace(" ", "").Contains("//TODO:");
		else
			return text.Contains("- ");
	}

	/// <summary>Stores excluded script names</summary>
	[Serializable]
	public class ExcludedList
	{
		public List<string> scriptsNames;

		public ExcludedList() => scriptsNames = new List<string>();
	}

	/// <summary>Represents a task from a file</summary>
	public class TextTodo
	{
		public string fileName;
		public string text;

		public TextTodo(TextAsset source, string text)
		{
			fileName = source.name;
			this.text = text;
		}
	}

	/// <summary>Represents a task from a .todo file</summary>
	public class Todo : TextTodo
	{
		public Action Open;

		public Todo(TextAsset source, string text, int lineIndex) : base(source, text)
		{
			Open = () => AssetDatabase.OpenAsset(source, lineIndex);
		}
	}
}

/// <summary>Editor window to access the list of "TODO" tasks from the ToDoCore</summary>
class TodoList : EditorWindow
{
	static TodoList window;

	GUIStyle centeredStyle;
	GUIStyle boldStyle;
	GUIStyle boldButtonStyle;

	TextTodo randomToDo;
	Vector2 scrollPos;
	Vector2 excludeScroll;
	int todoCount;
	int selectedScriptIndex;

	[MenuItem("Tools/ToDoList")]
	static void ShowWindow()
	{
		window = GetWindow<TodoList>();
		window.titleContent = new GUIContent(nameof(TodoList));
		window.Show();
	}

	public static GUILayoutOption GetMaxWidth() => GUILayout.MaxWidth(window != null ? window.position.width : 0);

	public void GenerateRequirement()
	{
		TodoCore.Init();

		if (centeredStyle == null)
			centeredStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };

		if (boldStyle == null)
			boldStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };

		if (boldButtonStyle == null)
		{
			boldButtonStyle = new GUIStyle(GUI.skin.box)
			{
				fontStyle = FontStyle.Bold,
				stretchWidth = true,
				alignment = TextAnchor.MiddleLeft
			};
		}
	}

	void OnGUI()
	{
		GenerateRequirement();

		EditorGUILayout.BeginVertical();
		{
			EditorGUILayout.LabelField("ToDo list", boldCenteredStyle);
			EditorGUILayout.Space();

			if (!TodoCore.needsReload)
			{
				DisplayRandomPicker();

				EditorGUILayout.Space();

				DisplayList();

				EditorGUILayout.Space();

				EditorGUILayout.LabelField("Task count : " + todoCount, centeredStyle);

				Center(() =>
				{
					TodoCore.hideInspectorToDos = EditorGUILayout.Toggle(
						"Hide editors todos",
						TodoCore.hideInspectorToDos
					);
				}, true);

				EditorGUILayout.Space();

				DisplayExclusions();

				EditorGUILayout.Space();

				Center(() =>
				{
					if (GUILayout.Button("Refresh"))
						TodoCore.LoadAllToDos();
				}, true);
			}

			EditorGUILayout.Space();
		}
		EditorGUILayout.EndVertical();
	}

	void DisplayRandomPicker()
	{
		Center(() =>
		{
			if (GUILayout.Button("Random"))
			{
				List<TextTodo> totalTodoList = new List<TextTodo>();
				totalTodoList.AddRange(TodoCore.GetFileTodos());
				totalTodoList.AddRange(TodoCore.GetScriptTodos());

				randomToDo = totalTodoList[Random.Range(0, totalTodoList.Count)];
			}

			if (randomToDo != null &&
				GUILayout.Button(randomToDo.fileName + " : " + randomToDo.text, EditorStyles.label))
			{
				if (randomToDo is Todo)
					(randomToDo as Todo).Open();
			}
		}, false);
	}

	void DisplayList()
	{
		scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUIStyle.none, GUI.skin.verticalScrollbar);
		{
			EditorGUILayout.BeginVertical(new GUIStyle(GUI.skin.box));
			{
				todoCount = 0;
				Dictionary<string, List<TextTodo>> groupedTextTodos = new Dictionary<string, List<TextTodo>>();

				foreach (TextTodo todo in TodoCore.GetFileTodos())
				{
					if (!groupedTextTodos.ContainsKey(todo.fileName))
						groupedTextTodos.Add(todo.fileName, new List<TextTodo>());

					groupedTextTodos[todo.fileName].Add(todo);
				}

				foreach (KeyValuePair<string, List<TextTodo>> group in groupedTextTodos)
				{
					EditorGUILayout.LabelField(group.Key, boldButtonStyle);
					todoCount += group.Value.Count;

					foreach (TextTodo todo in group.Value)
						DisplayTodo(todo, false);

					EditorGUILayout.Space();
				}

				Dictionary<string, List<Todo>> groupedScriptTodos = new Dictionary<string, List<Todo>>();

				foreach (Todo todo in TodoCore.GetScriptTodos())
				{
					if (!groupedScriptTodos.ContainsKey(todo.fileName))
						groupedScriptTodos.Add(todo.fileName, new List<Todo>());

					groupedScriptTodos[todo.fileName].Add(todo);
				}

				foreach (KeyValuePair<string, List<Todo>> group in groupedScriptTodos)
				{
					EditorGUILayout.LabelField(group.Key + ".cs", boldButtonStyle);
					todoCount += group.Value.Count;

					foreach (Todo todo in group.Value)
						DisplayTodo(todo, true);

					EditorGUILayout.Space();
				}
			}
			EditorGUILayout.EndVertical();
		}
		EditorGUILayout.EndScrollView();
	}

	void DisplayExclusions()
	{
		EditorGUILayout.BeginVertical(new GUIStyle(GUI.skin.box));
		{
			EditorGUILayout.LabelField("Excluded scripts", boldCenteredStyle);
			EditorGUILayout.Space();

			excludeScroll = EditorGUILayout.BeginScrollView(excludeScroll);
			{
				string toRemove = string.Empty;

				foreach (string script in TodoCore.excludedScripts.scriptsNames)
				{
					Center(() =>
					{
						EditorGUILayout.LabelField(script);

						if (GUILayout.Button("Remove"))
							toRemove = script;
					}, false);
				}

				if (!string.IsNullOrEmpty(toRemove))
				{
					excludedScripts.scriptsNames.Remove(toRemove);
					TodoCore.SaveExcludes();
				}
			}
			EditorGUILayout.EndScrollView();

			EditorGUILayout.Space();

			EditorGUILayout.BeginHorizontal();
			{
				List<string> fileNames = new List<string>();
				string[] scriptsGUIDs = AssetDatabase.FindAssets("t:Script");

				foreach (string guid in scriptsGUIDs)
				{
					string scriptPath = AssetDatabase.GUIDToAssetPath(guid);

					if (scriptPath.Contains("Packages"))
					{
						TextAsset script = AssetDatabase.LoadAssetAtPath<TextAsset>(scriptPath);

						if (!excludedScripts.scriptsNames.Contains(script.name))
							fileNames.Add(script.name);
					}
				}

				if (fileNames.Count > 0)
				{
					selectedScriptIndex = EditorGUILayout.Popup(selectedScriptIndex, fileNames.ToArray());

					if (GUILayout.Button("Add exception"))
					{
						excludedScripts.scriptsNames.Add(fileNames[selectedScriptIndex]);
						selectedScriptIndex = 0;

						TodoCore.RemoveTodosForScript(fileNames[selectedScriptIndex]);
					}
				}
			}
			EditorGUILayout.EndHorizontal();
		}
		EditorGUILayout.EndVertical();
	}

	void Center(Action GUI, bool withSpaces)
	{
		EditorGUILayout.BeginHorizontal();
		{
			if (withSpaces)
				EditorGUILayout.Space();

			GUI?.Invoke();

			if (withSpaces)
				EditorGUILayout.Space();
		}
		EditorGUILayout.EndHorizontal();
	}
}

/// <summary>Custom editor to display "TODO" tasks for the currently inspected component</summary>
[CustomEditor(typeof(MonoBehaviour), true)]
class ToDoEditor : Editor
{
	public override void OnInspectorGUI()
	{
		if (!TodoCore.hideInspectorToDos)
		{
			TodoCore.Init();
			List<Todo> todos = TodoCore.GetComponentTodos(target.GetType());

			if (todos.Count > 0)
			{
				EditorGUILayout.LabelField("ToDos", TodoCore.boldCenteredStyle);

				todos.ForEach(item => TodoCore.DisplayTodo(item, true));

				EditorGUILayout.Space();
			}
		}

		base.OnInspectorGUI();
	}
}

/// <summary>Custom editor to display "TODO" tasks for the currently inspected scriptable object</summary>
[CustomEditor(typeof(ScriptableObject), true)]
class ToDoObjEditor : Editor
{
	public override void OnInspectorGUI()
	{
		if (!TodoCore.hideInspectorToDos)
		{
			TodoCore.Init();
			List<Todo> todos = TodoCore.GetComponentTodos(target.GetType());

			if (todos.Count > 0)
			{
				EditorGUILayout.LabelField("ToDos", TodoCore.boldCenteredStyle);

				todos.ForEach(item => TodoCore.DisplayTodo(item, true));

				EditorGUILayout.Space();
			}
		}

		base.OnInspectorGUI();
	}
}