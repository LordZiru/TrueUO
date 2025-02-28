#region References
using Server.Commands.Generic;
using Server.Network;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
#endregion

namespace Server.Commands
{
    public class Docs
    {
        public static void Initialize()
        {
            CommandSystem.Register("DocGen", AccessLevel.Administrator, DocGen_OnCommand);
        }

        [Usage("DocGen")]
        [Description("Generates TrueUO documentation.")]
        private static void DocGen_OnCommand(CommandEventArgs e)
        {
            World.Broadcast(0x35, true, "Documentation is being generated, please wait.");
            Console.WriteLine("Documentation is being generated, please wait.");

            NetState.FlushAll();
            NetState.Pause();

            DateTime startTime = DateTime.UtcNow;

            bool generated = Document();

            DateTime endTime = DateTime.UtcNow;

            NetState.Resume();

            if (generated)
            {
                World.Broadcast(
                    0x35,
                    true,
                    "Documentation has been completed. The entire process took {0:F1} seconds.",
                    (endTime - startTime).TotalSeconds);
                Console.WriteLine("Documentation complete.");
            }
            else
            {
                World.Broadcast(
                    0x35,
                    true,
                    "Docmentation failed: Documentation directories are locked and in use. Please close all open files and directories and try again.");
                Console.WriteLine("Documentation failed.");
            }
        }

        private class MemberComparer : IComparer
        {
            public int Compare(object x, object y)
            {
                if (x == y)
                {
                    return 0;
                }

                ConstructorInfo aCtor = x as ConstructorInfo;
                ConstructorInfo bCtor = y as ConstructorInfo;

                PropertyInfo aProp = x as PropertyInfo;
                PropertyInfo bProp = y as PropertyInfo;

                MethodInfo aMethod = x as MethodInfo;
                MethodInfo bMethod = y as MethodInfo;

                bool aStatic = GetStaticFor(aCtor, aProp, aMethod);
                bool bStatic = GetStaticFor(bCtor, bProp, bMethod);

                if (aStatic && !bStatic)
                {
                    return -1;
                }

                if (!aStatic && bStatic)
                {
                    return 1;
                }

                int v = 0;

                if (aCtor != null)
                {
                    if (bCtor == null)
                    {
                        v = -1;
                    }
                }
                else if (bCtor != null)
                {
                    v = 1;
                }
                else if (aProp != null)
                {
                    if (bProp == null)
                    {
                        v = -1;
                    }
                }
                else if (bProp != null)
                {
                    v = 1;
                }

                if (v == 0)
                {
                    v = string.Compare(
                        GetNameFrom(aCtor, aProp, aMethod),
                        GetNameFrom(bCtor, bProp, bMethod),
                        StringComparison.Ordinal);
                }

                if (v == 0 && aCtor != null && bCtor != null)
                {
                    v = aCtor.GetParameters().Length.CompareTo(bCtor.GetParameters().Length);
                }
                else if (v == 0 && aMethod != null && bMethod != null)
                {
                    v = aMethod.GetParameters().Length.CompareTo(bMethod.GetParameters().Length);
                }

                return v;
            }

            private static bool GetStaticFor(ConstructorInfo ctor, PropertyInfo prop, MethodInfo method)
            {
                if (ctor != null)
                {
                    return ctor.IsStatic;
                }

                if (method != null)
                {
                    return method.IsStatic;
                }

                if (prop != null)
                {
                    MethodInfo getMethod = prop.GetGetMethod();
                    MethodInfo setMethod = prop.GetGetMethod();

                    return getMethod != null && getMethod.IsStatic || setMethod != null && setMethod.IsStatic;
                }

                return false;
            }

            private static string GetNameFrom(ConstructorInfo ctor, PropertyInfo prop, MethodInfo method)
            {
                if (ctor != null)
                {
                    if (ctor.DeclaringType != null)
                    {
                        return ctor.DeclaringType.Name;
                    }

                    return ctor.Name;
                }

                if (prop != null)
                {
                    return prop.Name;
                }

                if (method != null)
                {
                    return method.Name;
                }

                return "";
            }
        }

        private class TypeComparer : IComparer<TypeInfo>
        {
            public int Compare(TypeInfo x, TypeInfo y)
            {
                if (x == null && y == null)
                {
                    return 0;
                }

                if (x == null)
                {
                    return -1;
                }

                if (y == null)
                {
                    return 1;
                }

                return string.Compare(x.TypeName, y.TypeName, StringComparison.Ordinal);
            }
        }

        private class TypeInfo
        {
            public readonly Type m_Type;
            public readonly Type m_BaseType;
            public readonly Type m_Declaring;
            public List<TypeInfo> m_Derived, m_Nested;
            public readonly Type[] m_Interfaces;
            private readonly string m_FileName;
            private readonly string m_TypeName;
            private readonly string m_LinkName;

            public TypeInfo(Type type)
            {
                m_Type = type;

                m_BaseType = type.BaseType;
                m_Declaring = type.DeclaringType;
                m_Interfaces = type.GetInterfaces();

                FormatGeneric(m_Type, out m_TypeName, out m_FileName, out m_LinkName);
            }

            public string FileName => m_FileName;
            public string TypeName => m_TypeName;

            public string LinkName(string dirRoot)
            {
                return m_LinkName.Replace("@directory@", dirRoot);
            }
        }

        #region FileSystem
        private static readonly char[] ReplaceChars = "<>".ToCharArray();

        public static string GetFileName(string root, string name, string ext)
        {
            if (name.IndexOfAny(ReplaceChars) >= 0)
            {
                StringBuilder sb = new StringBuilder(name);

                foreach (char c in ReplaceChars)
                {
                    sb.Replace(c, '-');
                }

                name = sb.ToString();
            }

            int index = 0;
            string file = string.Concat(name, ext);

            while (File.Exists(Path.Combine(root, file)))
            {
                file = string.Concat(name, ++index, ext);
            }

            return file;
        }

        private static readonly string m_RootDirectory = Path.GetDirectoryName(Environment.GetCommandLineArgs()[0]);

        private static void EnsureDirectory(string path)
        {
            path = Path.Combine(m_RootDirectory, path);

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        private static void DeleteDirectory(string path)
        {
            path = Path.Combine(m_RootDirectory, path);

            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }

        private static StreamWriter GetWriter(string root, string name)
        {
            return new StreamWriter(Path.Combine(Path.Combine(m_RootDirectory, root), name));
        }

        private static StreamWriter GetWriter(string path)
        {
            return new StreamWriter(Path.Combine(m_RootDirectory, path));
        }
        #endregion

        #region GetPair
        private static readonly string[,] m_Aliases =
        {
            {"System.Object", "<span style=\"color: blue;\">object</span>"},
            {"System.String", "<span style=\"color: blue;\">string</span>"},
            {"System.Boolean", "<span style=\"color: blue;\">bool</span>"},
            {"System.Byte", "<span style=\"color: blue;\">byte</span>"},
            {"System.SByte", "<span style=\"color: blue;\">sbyte</span>"},
            {"System.Int16", "<span style=\"color: blue;\">short</span>"},
            {"System.UInt16", "<span style=\"color: blue;\">ushort</span>"},
            {"System.Int32", "<span style=\"color: blue;\">int</span>"},
            {"System.UInt32", "<span style=\"color: blue;\">uint</span>"},
            {"System.Int64", "<span style=\"color: blue;\">long</span>"},
            {"System.UInt64", "<span style=\"color: blue;\">ulong</span>"},
            {"System.Single", "<span style=\"color: blue;\">float</span>"},
            {"System.Double", "<span style=\"color: blue;\">double</span>"},
            {"System.Decimal", "<span style=\"color: blue;\">decimal</span>"},
            {"System.Char", "<span style=\"color: blue;\">char</span>"},
            {"System.Void", "<span style=\"color: blue;\">void</span>"}
        };

        private static readonly int m_AliasLength = m_Aliases.GetLength(0);

        public static string GetPair(Type varType, string name, bool ignoreRef)
        {
            string prepend = "";
            StringBuilder append = new StringBuilder();

            Type realType = varType;

            if (varType.IsByRef)
            {
                if (!ignoreRef)
                {
                    prepend = RefString;
                }

                realType = varType.GetElementType();
            }

            if (realType.IsPointer)
            {
                if (realType.IsArray)
                {
                    append.Append('*');

                    do
                    {
                        append.Append('[');

                        for (int i = 1; i < realType.GetArrayRank(); ++i)
                        {
                            append.Append(',');
                        }

                        append.Append(']');

                        realType = realType.GetElementType();
                    }
                    while (realType.IsArray);

                    append.Append(' ');
                }
                else
                {
                    realType = realType.GetElementType();
                    append.Append(" *");
                }
            }
            else if (realType.IsArray)
            {
                do
                {
                    append.Append('[');

                    for (int i = 1; i < realType.GetArrayRank(); ++i)
                    {
                        append.Append(',');
                    }

                    append.Append(']');

                    realType = realType.GetElementType();
                }
                while (realType.IsArray);

                append.Append(' ');
            }
            else
            {
                append.Append(' ');
            }

            string fullName = realType.FullName;
            string aliased = null; // = realType.Name;

            TypeInfo info = null;
            m_Types.TryGetValue(realType, out info);

            if (info != null)
            {
                aliased = "<!-- DBG-0 -->" + info.LinkName(null);
                //aliased = String.Format( "<a href=\"{0}\">{1}</a>", info.m_FileName, info.m_TypeName );
            }
            else
            {
                //FormatGeneric( );
                if (realType.IsGenericType)
                {
                    string typeName, fileName, linkName;
                    FormatGeneric(realType, out typeName, out fileName, out linkName);
                    linkName = linkName.Replace("@directory@", null);
                    aliased = linkName;
                }
                else
                {
                    for (int i = 0; i < m_AliasLength; ++i)
                    {
                        if (m_Aliases[i, 0] == fullName)
                        {
                            aliased = m_Aliases[i, 1];
                            break;
                        }
                    }
                }

                if (aliased == null)
                {
                    aliased = realType.Name;
                }
            }

            string retval = string.Concat(prepend, aliased, append, name);
            //Console.WriteLine(">> getpair: "+retval);
            return retval;
        }
        #endregion

        private static Dictionary<Type, TypeInfo> m_Types;
        private static Dictionary<string, List<TypeInfo>> m_Namespaces;

        #region Root documentation
        private static bool Document()
        {
            try
            {
                DeleteDirectory("docs/");
            }
            catch (Exception e)
            {
                Diagnostics.ExceptionLogging.LogException(e);
                return false;
            }

            EnsureDirectory("docs/");
            EnsureDirectory("docs/namespaces/");
            EnsureDirectory("docs/types/");

            GenerateStyles();
            GenerateIndex();

            DocumentCommands();
            DocumentKeywords();
           

            m_Types = new Dictionary<Type, TypeInfo>();
            m_Namespaces = new Dictionary<string, List<TypeInfo>>();

            List<Assembly> assemblies = new List<Assembly>
            {
                Core.Assembly
            };

            assemblies.AddRange(ScriptCompiler.Assemblies);

            Assembly[] asms = assemblies.ToArray();

            foreach (Assembly a in asms)
            {
                LoadTypes(a, asms);
            }

            DocumentLoadedTypes();
            DocumentConstructableObjects();

            return true;
        }

        private static void AddIndexLink(StreamWriter html, string filePath, string label, string desc)
        {
            html.WriteLine("      <h2><a href=\"{0}\" title=\"{1}\">{2}</a></h2>", filePath, desc, label);
        }

        private static void GenerateStyles()
        {
            using (StreamWriter css = GetWriter("docs/", "styles.css"))
            {
                css.WriteLine("body { background-color: #FFFFFF; font-family: verdana, arial; font-size: 11px; }");
                css.WriteLine("a { color: #28435E; }");
                css.WriteLine("a:hover { color: #4878A9; }");
                css.WriteLine("td.header { background-color: #9696AA; font-weight: bold; font-size: 12px; }");
                css.WriteLine("td.lentry { background-color: #D7D7EB; width: 10%; }");
                css.WriteLine("td.rentry { background-color: #FFFFFF; width: 90%; }");
                css.WriteLine("td.entry { background-color: #FFFFFF; }");
                css.WriteLine("td { font-size: 11px; }");
                css.WriteLine(".tbl-border { background-color: #46465A; }");
            }
        }

        private static void GenerateIndex()
        {
            using (StreamWriter html = GetWriter("docs/", "index.html"))
            {
                html.WriteLine("<!DOCTYPE html>");
                html.WriteLine("<html>");
                html.WriteLine("   <head>");
                html.WriteLine("      <title>TrueUO Documentation - Index</title>");
                html.WriteLine("      <style type=\"text/css\">");
                html.WriteLine("      body { background-color: white; font-family: Tahoma; color: #000000; }");
                html.WriteLine("      a, a:visited { color: #000000; }");
                html.WriteLine("      a:active, a:hover { color: #808080; }");
                html.WriteLine("      </style>");
                html.WriteLine("      <link rel=\"stylesheet\" type=\"text/css\" href=\"styles.css\" />");
                html.WriteLine("   </head>");
                html.WriteLine("   <body>");

                AddIndexLink(
                    html,
                    "commands.html",
                    "Commands",
                    "Every available command. This contains command name, usage, aliases, and description.");
                AddIndexLink(
                    html,
                    "objects.html",
                    "Constructable Objects",
                    "Every constructable item or npc. This contains object name and usage. Hover mouse over parameters to see type description.");
                AddIndexLink(
                    html,
                    "keywords.html",
                    "Speech Keywords",
                    "Lists speech keyword numbers and associated match patterns. These are used in some scripts for multi-language matching of client speech.");
                AddIndexLink(
                    html,
                    "bodies.html",
                    "Body List",
                    "Every usable body number and name. Table is generated from a UO:3D client datafile. If you do not have UO:3D installed, this may be blank.");
                AddIndexLink(
                    html,
                    "overview.html",
                    "Class Overview",
                    "Scripting reference. Contains every class type and contained methods in the core and scripts.");

                html.WriteLine("   </body>");
                html.WriteLine("</html>");
            }
        }
        #endregion

        #region Speech
        private static void DocumentKeywords()
        {
            List<Dictionary<int, SpeechEntry>> tables = LoadSpeechFile();

            using (StreamWriter html = GetWriter("docs/", "keywords.html"))
            {
                html.WriteLine("<!DOCTYPE html>");
                html.WriteLine("<html>");
                html.WriteLine("   <head>");
                html.WriteLine("      <title>TrueUO Documentation - Speech Keywords</title>");
                html.WriteLine("      <style type=\"text/css\">");
                html.WriteLine("      body { background-color: white; font-family: Tahoma; color: #000000; }");
                html.WriteLine("      a, a:visited { color: #000000; }");
                html.WriteLine("      a:active, a:hover { color: #808080; }");
                html.WriteLine("      table { width: 100%; }");
                html.WriteLine("      </style>");
                html.WriteLine("      <link rel=\"stylesheet\" type=\"text/css\" href=\"styles.css\" />");
                html.WriteLine("   </head>");
                html.WriteLine("   <body>");
                html.WriteLine("      <h4><a href=\"index.html\">Back to the index</a></h4>");
                html.WriteLine("      <h2>Speech Keywords</h2>");

                for (int p = 0; p < 1 && p < tables.Count; ++p)
                {
                    Dictionary<int, SpeechEntry> table = tables[p];

                    html.WriteLine("      <table cellpadding=\"0\" cellspacing=\"0\" border=\"0\">");
                    html.WriteLine("      <tr><td class=\"tbl-border\">");
                    html.WriteLine("      <table cellpadding=\"4\" cellspacing=\"1\">");
                    html.WriteLine("         <tr><td class=\"header\">Number</td><td class=\"header\">Text</td></tr>");

                    List<SpeechEntry> list = new List<SpeechEntry>(table.Values);
                    list.Sort(new SpeechEntrySorter());

                    foreach (SpeechEntry entry in list)
                    {
                        html.Write("         <tr><td class=\"lentry\">0x{0:X4}</td><td class=\"rentry\">", entry.Index);

                        entry.Strings.Sort(); //( new EnglishPrioStringSorter() );

                        for (int j = 0; j < entry.Strings.Count; ++j)
                        {
                            if (j > 0)
                            {
                                html.Write("<br />");
                            }

                            string v = entry.Strings[j];

                            foreach (char c in v)
                            {
                                switch (c)
                                {
                                    case '<':
                                        html.Write("&lt;");
                                        break;
                                    case '>':
                                        html.Write("&gt;");
                                        break;
                                    case '&':
                                        html.Write("&amp;");
                                        break;
                                    case '"':
                                        html.Write("&quot;");
                                        break;
                                    case '\'':
                                        html.Write("&apos;");
                                        break;
                                    default:
                                        {
                                            if (c >= 0x20 && c < 0x80)
                                            {
                                                html.Write(c);
                                            }
                                            else
                                            {
                                                html.Write("&#{0};", (int)c);
                                            }
                                        }
                                        break;
                                }
                            }
                        }

                        html.WriteLine("</td></tr>");
                    }

                    html.WriteLine("      </table></td></tr></table>");
                }

                html.WriteLine("   </body>");
                html.WriteLine("</html>");
            }
        }

        private class SpeechEntry
        {
            private readonly int m_Index;
            private readonly List<string> m_Strings;

            public int Index => m_Index;
            public List<string> Strings => m_Strings;

            public SpeechEntry(int index)
            {
                m_Index = index;
                m_Strings = new List<string>();
            }
        }

        private class SpeechEntrySorter : IComparer<SpeechEntry>
        {
            public int Compare(SpeechEntry x, SpeechEntry y)
            {
                return x.Index.CompareTo(y.Index);
            }
        }

        private static List<Dictionary<int, SpeechEntry>> LoadSpeechFile()
        {
            List<Dictionary<int, SpeechEntry>> tables = new List<Dictionary<int, SpeechEntry>>();
            int lastIndex = -1;

            Dictionary<int, SpeechEntry> table = null;

            string path = Core.FindDataFile("speech.mul");

            if (File.Exists(path))
            {
                using (FileStream ip = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    BinaryReader bin = new BinaryReader(ip);

                    while (bin.PeekChar() >= 0)
                    {
                        int index = (bin.ReadByte() << 8) | bin.ReadByte();
                        int length = (bin.ReadByte() << 8) | bin.ReadByte();
                        string text = Encoding.UTF8.GetString(bin.ReadBytes(length)).Trim();

                        if (text.Length == 0)
                        {
                            continue;
                        }

                        if (table == null || lastIndex > index)
                        {
                            if (index == 0 && text == "*withdraw*")
                            {
                                tables.Insert(0, table = new Dictionary<int, SpeechEntry>());
                            }
                            else
                            {
                                tables.Add(table = new Dictionary<int, SpeechEntry>());
                            }
                        }

                        lastIndex = index;

                        SpeechEntry entry;
                        table.TryGetValue(index, out entry);

                        if (entry == null)
                        {
                            table[index] = entry = new SpeechEntry(index);
                        }

                        entry.Strings.Add(text);
                    }
                }
            }

            return tables;
        }
        #endregion

        #region Commands
        public class DocCommandEntry
        {
            private readonly AccessLevel m_AccessLevel;
            private readonly string m_Name;
            private readonly string[] m_CmdAliases;
            private readonly string m_Usage;
            private readonly string m_Description;

            public AccessLevel AccessLevel => m_AccessLevel;
            public string Name => m_Name;
            public string[] Aliases => m_CmdAliases;
            public string Usage => m_Usage;
            public string Description => m_Description;

            public DocCommandEntry(AccessLevel accessLevel, string name, string[] aliases, string usage, string description)
            {
                m_AccessLevel = accessLevel;
                m_Name = name;
                m_CmdAliases = aliases;
                m_Usage = usage;
                m_Description = description;
            }
        }

        public class CommandEntrySorter : IComparer<DocCommandEntry>
        {
            public int Compare(DocCommandEntry a, DocCommandEntry b)
            {
                int v = b.AccessLevel.CompareTo(a.AccessLevel);

                if (v == 0)
                {
                    v = string.Compare(a.Name, b.Name, StringComparison.Ordinal);
                }

                return v;
            }
        }

        private static void DocumentCommands()
        {
            using (StreamWriter html = GetWriter("docs/", "commands.html"))
            {
                html.WriteLine("<!DOCTYPE html>");
                html.WriteLine("<html>");
                html.WriteLine("   <head>");
                html.WriteLine("      <title>TrueUO Documentation - Commands</title>");
                html.WriteLine("      <style type=\"text/css\">");
                html.WriteLine("      body { background-color: white; font-family: Tahoma; color: #000000; }");
                html.WriteLine("      a, a:visited { color: #000000; }");
                html.WriteLine("      a:active, a:hover { color: #808080; }");
                html.WriteLine("      table { width: 100%; }");
                html.WriteLine("      td.header { width: 100%; }");
                html.WriteLine("      </style>");
                html.WriteLine("      <link rel=\"stylesheet\" type=\"text/css\" href=\"styles.css\" />");
                html.WriteLine("   </head>");
                html.WriteLine("   <body>");
                html.WriteLine("      <a name=\"Top\" />");
                html.WriteLine("      <h4><a href=\"index.html\">Back to the index</a></h4>");
                html.WriteLine("      <h2>Commands</h2>");

                List<CommandEntry> commands = new List<CommandEntry>(CommandSystem.Entries.Values);
                List<DocCommandEntry> list = new List<DocCommandEntry>();

                commands.Sort();
                commands.Reverse();
                Clean(commands);

                foreach (CommandEntry e in commands)
                {
                    MethodInfo mi = e.Handler.Method;

                    object[] attrs = mi.GetCustomAttributes(typeof(UsageAttribute), false);

                    if (attrs.Length == 0)
                    {
                        continue;
                    }

                    UsageAttribute usage = attrs[0] as UsageAttribute;

                    attrs = mi.GetCustomAttributes(typeof(DescriptionAttribute), false);

                    if (attrs.Length == 0)
                    {
                        continue;
                    }

                    DescriptionAttribute desc = attrs[0] as DescriptionAttribute;

                    if (usage == null || desc == null)
                    {
                        continue;
                    }

                    attrs = mi.GetCustomAttributes(typeof(AliasesAttribute), false);

                    AliasesAttribute aliases = attrs.Length == 0 ? null : attrs[0] as AliasesAttribute;

                    string descString = desc.Description.Replace("<", "&lt;").Replace(">", "&gt;");

                    list.Add(
                        aliases == null
                            ? new DocCommandEntry(e.AccessLevel, e.Command, null, usage.Usage, descString)
                            : new DocCommandEntry(e.AccessLevel, e.Command, aliases.Aliases, usage.Usage, descString));
                }

                foreach (BaseCommand command in TargetCommands.AllCommands)
                {
                    string usage = command.Usage;
                    string desc = command.Description;

                    if (usage == null || desc == null)
                    {
                        continue;
                    }

                    string[] cmds = command.Commands;
                    string cmd = cmds[0];
                    string[] aliases = new string[cmds.Length - 1];

                    for (int j = 0; j < aliases.Length; ++j)
                    {
                        aliases[j] = cmds[j + 1];
                    }

                    desc = desc.Replace("<", "&lt;").Replace(">", "&gt;");

                    if (command.Supports != CommandSupport.Single)
                    {
                        StringBuilder sb = new StringBuilder(50 + desc.Length);

                        sb.Append("Modifiers: ");

                        if ((command.Supports & CommandSupport.Global) != 0)
                        {
                            sb.Append("<i><a href=\"#Global\">Global</a></i>, ");
                        }

                        if ((command.Supports & CommandSupport.Online) != 0)
                        {
                            sb.Append("<i><a href=\"#Online\">Online</a></i>, ");
                        }

                        if ((command.Supports & CommandSupport.Region) != 0)
                        {
                            sb.Append("<i><a href=\"#Region\">Region</a></i>, ");
                        }

                        if ((command.Supports & CommandSupport.Contained) != 0)
                        {
                            sb.Append("<i><a href=\"#Contained\">Contained</a></i>, ");
                        }

                        if ((command.Supports & CommandSupport.Multi) != 0)
                        {
                            sb.Append("<i><a href=\"#Multi\">Multi</a></i>, ");
                        }

                        if ((command.Supports & CommandSupport.Area) != 0)
                        {
                            sb.Append("<i><a href=\"#Area\">Area</a></i>, ");
                        }

                        if ((command.Supports & CommandSupport.Self) != 0)
                        {
                            sb.Append("<i><a href=\"#Self\">Self</a></i>, ");
                        }

                        sb.Remove(sb.Length - 2, 2);
                        sb.Append("<br />");
                        sb.Append(desc);

                        desc = sb.ToString();
                    }

                    list.Add(new DocCommandEntry(command.AccessLevel, cmd, aliases, usage, desc));
                }

                List<BaseCommandImplementor> commandImpls = BaseCommandImplementor.Implementors;

                foreach (BaseCommandImplementor command in commandImpls)
                {
                    string usage = command.Usage;
                    string desc = command.Description;

                    if (usage == null || desc == null)
                    {
                        continue;
                    }

                    string[] cmds = command.Accessors;
                    string cmd = cmds[0];
                    string[] aliases = new string[cmds.Length - 1];

                    for (int j = 0; j < aliases.Length; ++j)
                    {
                        aliases[j] = cmds[j + 1];
                    }

                    desc = desc.Replace("<", "&lt;").Replace(">", "&gt;");

                    list.Add(new DocCommandEntry(command.AccessLevel, cmd, aliases, usage, desc));
                }

                list.Sort(new CommandEntrySorter());

                AccessLevel last = AccessLevel.Player;

                foreach (DocCommandEntry e in list)
                {
                    if (e.AccessLevel != last)
                    {
                        if (last != AccessLevel.Player)
                        {
                            html.WriteLine("      </table></td></tr></table><br />");
                        }

                        last = e.AccessLevel;

                        html.WriteLine("      <a name=\"{0}\" />", last);

                        switch (last)
                        {
                            case AccessLevel.Administrator:
                                html.WriteLine(
                                    "      <b>Administrator</b> | <a href=\"#GameMaster\">Game Master</a> | <a href=\"#Counselor\">Counselor</a> | <a href=\"#Player\">Player</a><br /><br />");
                                break;
                            case AccessLevel.GameMaster:
                                html.WriteLine(
                                    "      <a href=\"#Top\">Administrator</a> | <b>Game Master</b> | <a href=\"#Counselor\">Counselor</a> | <a href=\"#Player\">Player</a><br /><br />");
                                break;
                            case AccessLevel.Seer:
                                html.WriteLine(
                                    "      <a href=\"#Top\">Administrator</a> | <a href=\"#GameMaster\">Game Master</a> | <a href=\"#Counselor\">Counselor</a> | <a href=\"#Player\">Player</a><br /><br />");
                                break;
                            case AccessLevel.Counselor:
                                html.WriteLine(
                                    "      <a href=\"#Top\">Administrator</a> | <a href=\"#GameMaster\">Game Master</a> | <b>Counselor</b> | <a href=\"#Player\">Player</a><br /><br />");
                                break;
                            case AccessLevel.Player:
                                html.WriteLine(
                                    "      <a href=\"#Top\">Administrator</a> | <a href=\"#GameMaster\">Game Master</a> | <a href=\"#Counselor\">Counselor</a> | <b>Player</b><br /><br />");
                                break;
                        }

                        html.WriteLine("      <table cellpadding=\"0\" cellspacing=\"0\" border=\"0\">");
                        html.WriteLine("      <tr><td class=\"tbl-border\">");
                        html.WriteLine("      <table cellpadding=\"4\" cellspacing=\"1\">");
                        html.WriteLine(
                            "         <tr><td colspan=\"2\" class=\"header\">{0}</td></tr>",
                            last == AccessLevel.GameMaster ? "Game Master" : last.ToString());
                    }

                    DocumentCommand(html, e);
                }

                html.WriteLine("      </table></td></tr></table>");
                html.WriteLine("   </body>");
                html.WriteLine("</html>");
            }
        }

        public static void Clean(List<CommandEntry> list)
        {
            for (int i = 0; i < list.Count; ++i)
            {
                CommandEntry e = list[i];

                for (int j = i + 1; j < list.Count; ++j)
                {
                    CommandEntry c = list[j];

                    if (e.Handler.Method == c.Handler.Method)
                    {
                        list.RemoveAt(j);
                        --j;
                    }
                }
            }
        }

        private static void DocumentCommand(StreamWriter html, DocCommandEntry e)
        {
            string usage = e.Usage;
            string desc = e.Description;
            string[] aliases = e.Aliases;

            html.Write("         <tr><a name=\"{0}\" /><td class=\"lentry\">{0}</td>", e.Name);

            if (aliases == null || aliases.Length == 0)
            {
                html.Write(
                    "<td class=\"rentry\"><b>Usage: {0}</b><br />{1}</td>",
                    usage.Replace("<", "&lt;").Replace(">", "&gt;"),
                    desc);
            }
            else
            {
                html.Write(
                    "<td class=\"rentry\"><b>Usage: {0}</b><br />Alias{1}: ",
                    usage.Replace("<", "&lt;").Replace(">", "&gt;"),
                    aliases.Length == 1 ? "" : "es");

                for (int i = 0; i < aliases.Length; ++i)
                {
                    if (i != 0)
                    {
                        html.Write(", ");
                    }

                    html.Write(aliases[i]);
                }

                html.Write("<br />{0}</td>", desc);
            }

            html.WriteLine("</tr>");
        }
        #endregion

        private static void LoadTypes(Assembly a, Assembly[] asms)
        {
            Type[] types = a.GetTypes();

            foreach (Type type in types)
            {
                string nspace = type.Namespace;

                if (nspace == null || type.IsSpecialName)
                {
                    continue;
                }

                TypeInfo info = new TypeInfo(type);
                m_Types[type] = info;

                List<TypeInfo> nspaces;
                m_Namespaces.TryGetValue(nspace, out nspaces);

                if (nspaces == null)
                {
                    m_Namespaces[nspace] = nspaces = new List<TypeInfo>();
                }

                nspaces.Add(info);

                Type baseType = info.m_BaseType;

                if (baseType != null && InAssemblies(baseType, asms))
                {
                    TypeInfo baseInfo;
                    m_Types.TryGetValue(baseType, out baseInfo);

                    if (baseInfo == null)
                    {
                        m_Types[baseType] = baseInfo = new TypeInfo(baseType);
                    }

                    if (baseInfo.m_Derived == null)
                    {
                        baseInfo.m_Derived = new List<TypeInfo>();
                    }

                    baseInfo.m_Derived.Add(info);
                }

                Type decType = info.m_Declaring;

                if (decType != null)
                {
                    TypeInfo decInfo;
                    m_Types.TryGetValue(decType, out decInfo);

                    if (decInfo == null)
                    {
                        m_Types[decType] = decInfo = new TypeInfo(decType);
                    }

                    if (decInfo.m_Nested == null)
                    {
                        decInfo.m_Nested = new List<TypeInfo>();
                    }

                    decInfo.m_Nested.Add(info);
                }

                foreach (Type iface in info.m_Interfaces)
                {
                    if (!InAssemblies(iface, asms))
                    {
                        continue;
                    }

                    TypeInfo ifaceInfo = null;
                    m_Types.TryGetValue(iface, out ifaceInfo);

                    if (ifaceInfo == null)
                    {
                        m_Types[iface] = ifaceInfo = new TypeInfo(iface);
                    }

                    if (ifaceInfo.m_Derived == null)
                    {
                        ifaceInfo.m_Derived = new List<TypeInfo>();
                    }

                    ifaceInfo.m_Derived.Add(info);
                }
            }
        }

        private static bool InAssemblies(Type t, IEnumerable<Assembly> asms)
        {
            return asms.Any(a => a == t.Assembly);
        }

        #region Constructable Objects
        private static readonly Type typeofItem = typeof(Item);
        private static readonly Type typeofMobile = typeof(Mobile);
        private static readonly Type typeofMap = typeof(Map);
        private static readonly Type typeofCustomEnum = typeof(CustomEnumAttribute);

        private static bool IsConstructable(Type t, out bool isItem)
        {
            isItem = typeofItem.IsAssignableFrom(t);

            if (isItem)
            {
                return true;
            }

            return typeofMobile.IsAssignableFrom(t);
        }

        private static bool IsConstructable(ConstructorInfo ctor)
        {
            return ctor.IsDefined(typeof(ConstructableAttribute), false);
        }

        private static void DocumentConstructableObjects()
        {
            List<TypeInfo> types = new List<TypeInfo>(m_Types.Values);
            types.Sort(new TypeComparer());

            ArrayList items = new ArrayList(), mobiles = new ArrayList();

            foreach (Type t in types.Select(ti => ti.m_Type))
            {
                bool isItem;

                if (t.IsAbstract || !IsConstructable(t, out isItem))
                {
                    continue;
                }

                ConstructorInfo[] ctors = t.GetConstructors();
                bool anyConstructable = false;

                for (int j = 0; !anyConstructable && j < ctors.Length; ++j)
                {
                    anyConstructable = IsConstructable(ctors[j]);
                }

                if (!anyConstructable)
                {
                    continue;
                }

                (isItem ? items : mobiles).Add(t);
                (isItem ? items : mobiles).Add(ctors);
            }

            using (StreamWriter html = GetWriter("docs/", "objects.html"))
            {
                html.WriteLine("<!DOCTYPE html>");
                html.WriteLine("<html>");
                html.WriteLine("   <head>");
                html.WriteLine("      <title>TrueUO Documentation - Constructable Objects</title>");
                html.WriteLine("      <style type=\"text/css\">");
                html.WriteLine("      body { background-color: white; font-family: Tahoma; color: #000000; }");
                html.WriteLine("      a, a:visited { color: #000000; }");
                html.WriteLine("      a:active, a:hover { color: #808080; }");
                html.WriteLine("      table { width: 100%; }");
                html.WriteLine("      </style>");
                html.WriteLine("      <link rel=\"stylesheet\" type=\"text/css\" href=\"styles.css\" />");
                html.WriteLine("   </head>");
                html.WriteLine("   <body>");
                html.WriteLine("      <h4><a href=\"index.html\">Back to the index</a></h4>");
                html.WriteLine("      <h2>Constructable <a href=\"#items\">Items</a> and <a href=\"#mobiles\">Mobiles</a></h2>");

                html.WriteLine("      <a name=\"items\" />");
                html.WriteLine("      <table cellpadding=\"0\" cellspacing=\"0\" border=\"0\">");
                html.WriteLine("      <tr><td class=\"tbl-border\">");
                html.WriteLine("      <table cellpadding=\"4\" cellspacing=\"1\">");
                html.WriteLine("         <tr><td class=\"header\">Item Name</td><td class=\"header\">Usage</td></tr>");

                for (int i = 0; i < items.Count; i += 2)
                {
                    DocumentConstructableObject(html, (Type)items[i], (ConstructorInfo[])items[i + 1]);
                }

                html.WriteLine("      </table></td></tr></table><br /><br />");

                html.WriteLine("      <a name=\"mobiles\" />");
                html.WriteLine("      <table width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\">");
                html.WriteLine("      <tr><td class=\"tbl-border\">");
                html.WriteLine("      <table width=\"100%\" cellpadding=\"4\" cellspacing=\"1\">");
                html.WriteLine("         <tr><td class=\"header\">Mobile Name</td><td class=\"header\">Usage</td></tr>");

                for (int i = 0; i < mobiles.Count; i += 2)
                {
                    DocumentConstructableObject(html, (Type)mobiles[i], (ConstructorInfo[])mobiles[i + 1]);
                }

                html.WriteLine("      </table></td></tr></table>");

                html.WriteLine("   </body>");
                html.WriteLine("</html>");
            }
        }

        private static void DocumentConstructableObject(StreamWriter html, Type t, IEnumerable<ConstructorInfo> ctors)
        {
            html.Write("         <tr><td class=\"lentry\">{0}</td><td class=\"rentry\">", t.Name);

            bool first = true;

            foreach (ConstructorInfo ctor in ctors.Where(IsConstructable))
            {
                if (!first)
                {
                    html.Write("<br />");
                }

                first = false;

                html.Write("{0}Add {1}", CommandSystem.Prefix, t.Name);

                ParameterInfo[] parms = ctor.GetParameters();

                foreach (ParameterInfo p in parms)
                {
                    html.Write(" <a ");

                    TypeInfo typeInfo;
                    m_Types.TryGetValue(p.ParameterType, out typeInfo);

                    if (typeInfo != null)
                    {
                        html.Write("href=\"types/{0}\" ", typeInfo.FileName);
                    }

                    html.Write("title=\"{0}\">{1}</a>", GetTooltipFor(p), p.Name);
                }
            }

            html.WriteLine("</td></tr>");
        }
        #endregion

        #region Tooltips
        private const string HtmlNewLine = "&#13;";

        private static readonly object[,] m_Tooltips =
        {
            {typeof(byte), "Numeric value in the range from 0 to 255, inclusive."},
            {typeof(sbyte), "Numeric value in the range from negative 128 to positive 127, inclusive."},
            {typeof(ushort), "Numeric value in the range from 0 to 65,535, inclusive."},
            {typeof(short), "Numeric value in the range from negative 32,768 to positive 32,767, inclusive."},
            {typeof(uint), "Numeric value in the range from 0 to 4,294,967,295, inclusive."},
            {typeof(int), "Numeric value in the range from negative 2,147,483,648 to positive 2,147,483,647, inclusive."},
            {typeof(ulong), "Numeric value in the range from 0 through about 10^20."},
            {typeof(long), "Numeric value in the approximate range from negative 10^19 through 10^19."},
            {
                typeof(string),
                "Text value. To specify a value containing spaces, encapsulate the value in quote characters:{0}{0}&quot;Spaced text example&quot;"
            },
            {typeof(bool), "Boolean value which can be either True or False."},
            {typeof(Map), "Map or facet name. Possible values include:{0}{0}- Felucca{0}- Trammel{0}- Ilshenar{0}- Malas"},
            {
                typeof(Poison),
                "Poison name or level. Possible values include:{0}{0}- Lesser{0}- Regular{0}- Greater{0}- Deadly{0}- Lethal"
            },
            {
                typeof(Point3D),
                "Three-dimensional coordinate value. Format as follows:{0}{0}&quot;(<x value>, <y value>, <z value>)&quot;"
            }
        };

        private static string GetTooltipFor(ParameterInfo param)
        {
            Type paramType = param.ParameterType;

            for (int i = 0; i < m_Tooltips.GetLength(0); ++i)
            {
                Type checkType = (Type)m_Tooltips[i, 0];

                if (paramType == checkType)
                {
                    return string.Format((string)m_Tooltips[i, 1], HtmlNewLine);
                }
            }

            if (paramType.IsEnum)
            {
                StringBuilder sb = new StringBuilder();

                sb.AppendFormat("Enumeration value or name. Possible named values include:{0}", HtmlNewLine);

                string[] names = Enum.GetNames(paramType);

                foreach (string n in names)
                {
                    sb.AppendFormat("{0}- {1}", HtmlNewLine, n);
                }

                return sb.ToString();
            }

            if (paramType.IsDefined(typeofCustomEnum, false))
            {
                object[] attributes = paramType.GetCustomAttributes(typeofCustomEnum, false);

                if (attributes.Length > 0 && attributes[0] is CustomEnumAttribute attr)
                {
                    StringBuilder sb = new StringBuilder();

                    sb.AppendFormat("Enumeration value or name. Possible named values include:{0}", HtmlNewLine);

                    string[] names = attr.Names;

                    for (var index = 0; index < names.Length; index++)
                    {
                        string n = names[index];

                        sb.AppendFormat("{0}- {1}", HtmlNewLine, n);
                    }

                    return sb.ToString();
                }
            }
            else if (paramType == typeofMap)
            {
                StringBuilder sb = new StringBuilder();

                sb.AppendFormat("Enumeration value or name. Possible named values include:{0}", HtmlNewLine);

                string[] names = Map.GetMapNames();

                for (var index = 0; index < names.Length; index++)
                {
                    string n = names[index];

                    sb.AppendFormat("{0}- {1}", HtmlNewLine, n);
                }

                return sb.ToString();
            }

            return "";
        }
        #endregion

        #region Const Strings
        private const string RefString = "<span style=\"color: blue;\">ref</span> ";
        private const string GetString = " <span style=\"color: blue;\">get</span>;";
        private const string SetString = " <span style=\"color: blue;\">set</span>;";

        private const string InString = "<span style=\"color: blue;\">in</span> ";
        private const string OutString = "<span style=\"color: blue;\">out</span> ";

        private const string VirtString = "<span style=\"color: blue;\">virtual</span> ";
        private const string CtorString = "(<span style=\"color: blue;\">ctor</span>) ";
        private const string StaticString = "(<span style=\"color: blue;\">static</span>) ";
        #endregion

        private static void DocumentLoadedTypes()
        {
            using (StreamWriter indexHtml = GetWriter("docs/", "overview.html"))
            {
                indexHtml.WriteLine("<!DOCTYPE html>");
                indexHtml.WriteLine("<html>");
                indexHtml.WriteLine("   <head>");
                indexHtml.WriteLine("      <title>TrueUO Documentation - Class Overview</title>");
                indexHtml.WriteLine("      <style type=\"text/css\">");
                indexHtml.WriteLine("      body { background-color: white; font-family: Tahoma; color: #000000; }");
                indexHtml.WriteLine("      a, a:visited { color: #000000; }");
                indexHtml.WriteLine("      a:active, a:hover { color: #808080; }");
                indexHtml.WriteLine("      </style>");
                indexHtml.WriteLine("   </head>");
                indexHtml.WriteLine("   <body>");
                indexHtml.WriteLine("      <h4><a href=\"index.html\">Back to the index</a></h4>");
                indexHtml.WriteLine("      <h2>Namespaces</h2>");

                SortedList<string, List<TypeInfo>> nspaces = new SortedList<string, List<TypeInfo>>(m_Namespaces);

                foreach (KeyValuePair<string, List<TypeInfo>> kvp in nspaces)
                {
                    kvp.Value.Sort(new TypeComparer());

                    SaveNamespace(kvp.Key, kvp.Value, indexHtml);
                }

                indexHtml.WriteLine("   </body>");
                indexHtml.WriteLine("</html>");
            }
        }

        private static void SaveNamespace(string name, IEnumerable<TypeInfo> types, StreamWriter indexHtml)
        {
            string fileName = GetFileName("docs/namespaces/", name, ".html");

            indexHtml.WriteLine("      <a href=\"namespaces/{0}\">{1}</a><br />", fileName, name);

            using (StreamWriter nsHtml = GetWriter("docs/namespaces/", fileName))
            {
                nsHtml.WriteLine("<!DOCTYPE html>");
                nsHtml.WriteLine("<html>");
                nsHtml.WriteLine("   <head>");
                nsHtml.WriteLine("      <title>TrueUO Documentation - Class Overview - {0}</title>", name);
                nsHtml.WriteLine("      <style type=\"text/css\">");
                nsHtml.WriteLine("      body { background-color: white; font-family: Tahoma; color: #000000; }");
                nsHtml.WriteLine("      a, a:visited { color: #000000; }");
                nsHtml.WriteLine("      a:active, a:hover { color: #808080; }");
                nsHtml.WriteLine("      </style>");
                nsHtml.WriteLine("   </head>");
                nsHtml.WriteLine("   <body>");
                nsHtml.WriteLine("      <h4><a href=\"../overview.html\">Back to the namespace index</a></h4>");
                nsHtml.WriteLine("      <h2>{0}</h2>", name);

                foreach (TypeInfo t in types)
                {
                    SaveType(t, nsHtml, fileName, name);
                }

                nsHtml.WriteLine("   </body>");
                nsHtml.WriteLine("</html>");
            }
        }

        private static void SaveType(TypeInfo info, StreamWriter nsHtml, string nsFileName, string nsName)
        {
            if (info.m_Declaring == null)
            {
                nsHtml.WriteLine("      <!-- DBG-ST -->" + info.LinkName("../types/") + "<br />");
            }

            using (StreamWriter typeHtml = GetWriter(info.FileName))
            {
                typeHtml.WriteLine("<!DOCTYPE html>");
                typeHtml.WriteLine("<html>");
                typeHtml.WriteLine("   <head>");
                typeHtml.WriteLine("      <title>TrueUO Documentation - Class Overview - {0}</title>", info.TypeName);
                typeHtml.WriteLine("      <style type=\"text/css\">");
                typeHtml.WriteLine("      body { background-color: white; font-family: Tahoma; color: #000000; }");
                typeHtml.WriteLine("      a, a:visited { color: #000000; }");
                typeHtml.WriteLine("      a:active, a:hover { color: #808080; }");
                typeHtml.WriteLine("      </style>");
                typeHtml.WriteLine("   </head>");
                typeHtml.WriteLine("   <body>");
                typeHtml.WriteLine("      <h4><a href=\"../namespaces/{0}\">Back to {1}</a></h4>", nsFileName, nsName);

                if (info.m_Type.IsEnum)
                {
                    WriteEnum(info, typeHtml);
                }
                else
                {
                    WriteType(info, typeHtml);
                }

                typeHtml.WriteLine("   </body>");
                typeHtml.WriteLine("</html>");
            }
        }

        #region Write[...]
        private static void WriteEnum(TypeInfo info, StreamWriter typeHtml)
        {
            Type type = info.m_Type;

            typeHtml.WriteLine("      <h2>{0} (Enum)</h2>", info.TypeName);

            string[] names = Enum.GetNames(type);

            bool flags = type.IsDefined(typeof(FlagsAttribute), false);

            string format = flags ? "      {0:G} = 0x{1:X}{2}<br />" : "      {0:G} = {1:D}{2}<br />";

            for (int i = 0; i < names.Length; ++i)
            {
                object value = Enum.Parse(type, names[i]);

                typeHtml.WriteLine(format, names[i], value, i < names.Length - 1 ? "," : "");
            }
        }

        private static void WriteType(TypeInfo info, StreamWriter typeHtml)
        {
            Type type = info.m_Type;

            typeHtml.Write("      <h2>");

            Type decType = info.m_Declaring;

            if (decType != null)
            {
                // We are a nested type

                typeHtml.Write('(');

                TypeInfo decInfo;
                m_Types.TryGetValue(decType, out decInfo);

                typeHtml.Write(decInfo == null ? decType.Name : decInfo.LinkName(null));

                typeHtml.Write(") - ");
            }

            typeHtml.Write(info.TypeName);

            Type[] ifaces = info.m_Interfaces;
            Type baseType = info.m_BaseType;

            int extendCount = 0;

            if (baseType != null && baseType != typeof(object) && baseType != typeof(ValueType) && !baseType.IsPrimitive)
            {
                typeHtml.Write(" : ");

                TypeInfo baseInfo;
                m_Types.TryGetValue(baseType, out baseInfo);

                if (baseInfo == null)
                {
                    typeHtml.Write(baseType.Name);
                }
                else
                {
                    typeHtml.Write("<!-- DBG-1 -->" + baseInfo.LinkName(null));
                }

                ++extendCount;
            }

            if (ifaces.Length > 0)
            {
                if (extendCount == 0)
                {
                    typeHtml.Write(" : ");
                }

                foreach (Type iface in ifaces)
                {
                    TypeInfo ifaceInfo;
                    m_Types.TryGetValue(iface, out ifaceInfo);

                    if (extendCount != 0)
                    {
                        typeHtml.Write(", ");
                    }

                    ++extendCount;

                    if (ifaceInfo == null)
                    {
                        string typeName, fileName, linkName;
                        FormatGeneric(iface, out typeName, out fileName, out linkName);
                        linkName = linkName.Replace("@directory@", null);
                        typeHtml.Write("<!-- DBG-2.1 -->" + linkName);
                    }
                    else
                    {
                        typeHtml.Write("<!-- DBG-2.2 -->" + ifaceInfo.LinkName(null));
                    }
                }
            }

            typeHtml.WriteLine("</h2>");

            List<TypeInfo> derived = info.m_Derived;

            if (derived != null)
            {
                typeHtml.Write("<h4>Derived Types: ");

                derived.Sort(new TypeComparer());

                for (int i = 0; i < derived.Count; ++i)
                {
                    TypeInfo derivedInfo = derived[i];

                    if (i != 0)
                    {
                        typeHtml.Write(", ");
                    }

                    //typeHtml.Write( "<a href=\"{0}\">{1}</a>", derivedInfo.m_FileName, derivedInfo.m_TypeName );
                    typeHtml.Write("<!-- DBG-3 -->" + derivedInfo.LinkName(null));
                }

                typeHtml.WriteLine("</h4>");
            }

            List<TypeInfo> nested = info.m_Nested;

            if (nested != null)
            {
                typeHtml.Write("<h4>Nested Types: ");

                nested.Sort(new TypeComparer());

                for (int i = 0; i < nested.Count; ++i)
                {
                    TypeInfo nestedInfo = nested[i];

                    if (i != 0)
                    {
                        typeHtml.Write(", ");
                    }

                    //typeHtml.Write( "<a href=\"{0}\">{1}</a>", nestedInfo.m_FileName, nestedInfo.m_TypeName );
                    typeHtml.Write("<!-- DBG-4 -->" + nestedInfo.LinkName(null));
                }

                typeHtml.WriteLine("</h4>");
            }

            MemberInfo[] membs =
                type.GetMembers(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance |
                    BindingFlags.DeclaredOnly);

            Array.Sort(membs, new MemberComparer());

            foreach (MemberInfo mi in membs)
            {
                if (mi is PropertyInfo propertyInfo)
                {
                    WriteProperty(propertyInfo, typeHtml);
                }
                else if (mi is ConstructorInfo constructorInfo)
                {
                    WriteCtor(info.TypeName, constructorInfo, typeHtml);
                }
                else if (mi is MethodInfo methodInfo)
                {
                    WriteMethod(methodInfo, typeHtml);
                }
            }
        }

        private static void WriteProperty(PropertyInfo pi, StreamWriter html)
        {
            html.Write("      ");

            MethodInfo getMethod = pi.GetGetMethod();
            MethodInfo setMethod = pi.GetSetMethod();

            if (getMethod != null && getMethod.IsStatic || setMethod != null && setMethod.IsStatic)
            {
                html.Write(StaticString);
            }

            html.Write(GetPair(pi.PropertyType, pi.Name, false));
            html.Write('(');

            if (pi.CanRead)
            {
                html.Write(GetString);
            }

            if (pi.CanWrite)
            {
                html.Write(SetString);
            }

            html.WriteLine(" )<br />");
        }

        private static void WriteCtor(string name, ConstructorInfo ctor, StreamWriter html)
        {
            if (ctor.IsStatic)
            {
                return;
            }

            html.Write("      ");
            html.Write(CtorString);
            html.Write(name);
            html.Write('(');

            ParameterInfo[] parms = ctor.GetParameters();

            if (parms.Length > 0)
            {
                html.Write(' ');

                for (int i = 0; i < parms.Length; ++i)
                {
                    ParameterInfo pi = parms[i];

                    if (i != 0)
                    {
                        html.Write(", ");
                    }

                    if (pi.IsIn)
                    {
                        html.Write(InString);
                    }
                    else if (pi.IsOut)
                    {
                        html.Write(OutString);
                    }

                    html.Write(GetPair(pi.ParameterType, pi.Name, pi.IsOut));
                }

                html.Write(' ');
            }

            html.WriteLine(")<br />");
        }

        private static void WriteMethod(MethodInfo mi, StreamWriter html)
        {
            if (mi.IsSpecialName)
            {
                return;
            }

            html.Write("      ");

            if (mi.IsStatic)
            {
                html.Write(StaticString);
            }

            if (mi.IsVirtual)
            {
                html.Write(VirtString);
            }

            html.Write(GetPair(mi.ReturnType, mi.Name, false));
            html.Write('(');

            ParameterInfo[] parms = mi.GetParameters();

            if (parms.Length > 0)
            {
                html.Write(' ');

                for (int i = 0; i < parms.Length; ++i)
                {
                    ParameterInfo pi = parms[i];

                    if (i != 0)
                    {
                        html.Write(", ");
                    }

                    if (pi.IsIn)
                    {
                        html.Write(InString);
                    }
                    else if (pi.IsOut)
                    {
                        html.Write(OutString);
                    }

                    html.Write(GetPair(pi.ParameterType, pi.Name, pi.IsOut));
                }

                html.Write(' ');
            }

            html.WriteLine(")<br />");
        }
        #endregion

        public static void FormatGeneric(Type type, out string typeName, out string fileName, out string linkName)
        {
            string name = null;
            string fnam = null;
            string link = null;

            if (type.IsGenericType)
            {
                int index = type.Name.IndexOf('`');

                if (index > 0)
                {
                    string rootType = type.Name.Substring(0, index);

                    StringBuilder nameBuilder = new StringBuilder(rootType);
                    StringBuilder fnamBuilder = new StringBuilder("docs/types/" + SanitizeType(rootType));

                    StringBuilder linkBuilder =
                        new StringBuilder(
                            DontLink(type)
                                ? "<span style=\"color: blue;\">" + rootType + "</span>"
                                : "<a href=\"" + "@directory@" + rootType + "-T-.html\">" + rootType + "</a>");

                    nameBuilder.Append("&lt;");
                    fnamBuilder.Append("-");
                    linkBuilder.Append("&lt;");

                    Type[] typeArguments = type.GetGenericArguments();

                    for (int i = 0; i < typeArguments.Length; i++)
                    {
                        if (i != 0)
                        {
                            nameBuilder.Append(',');
                            fnamBuilder.Append(',');
                            linkBuilder.Append(',');
                        }

                        string sanitizedName = SanitizeType(typeArguments[i].Name);
                        string aliasedName = AliasForName(sanitizedName);

                        nameBuilder.Append(sanitizedName);
                        fnamBuilder.Append("T");

                        if (DontLink(typeArguments[i])) //if( DontLink( typeArguments[i].Name ) )
                        {
                            linkBuilder.Append("<span style=\"color: blue;\">" + aliasedName + "</span>");
                        }
                        else
                        {
                            linkBuilder.Append("<a href=\"" + "@directory@" + aliasedName + ".html\">" + aliasedName + "</a>");
                        }
                    }

                    nameBuilder.Append("&gt;");
                    fnamBuilder.Append("-");
                    linkBuilder.Append("&gt;");

                    name = nameBuilder.ToString();
                    fnam = fnamBuilder.ToString();
                    link = linkBuilder.ToString();
                }
            }

            typeName = name ?? type.Name;

            if (fnam == null)
            {
                fileName = "docs/types/" + SanitizeType(type.Name) + ".html";
            }
            else
            {
                fileName = fnam + ".html";
            }

            if (link == null)
            {
                if (DontLink(type)) //if( DontLink( type.Name ) )
                {
                    linkName = "<span style=\"color: blue;\">" + SanitizeType(type.Name) + "</span>";
                }
                else
                {
                    linkName = "<a href=\"" + "@directory@" + SanitizeType(type.Name) + ".html\">" + SanitizeType(type.Name) + "</a>";
                }
            }
            else
            {
                linkName = link;
            }

            //Console.WriteLine( typeName+":"+fileName+":"+linkName );
        }

        public static string SanitizeType(string name)
        {
            bool anonymousType = name.Contains("<");
            StringBuilder sb = new StringBuilder(name);

            foreach (char c in ReplaceChars)
            {
                sb.Replace(c, '-');
            }

            if (anonymousType)
            {
                return "(Anonymous-Type)" + sb;
            }

            return sb.ToString();
        }

        public static string AliasForName(string name)
        {
            for (int i = 0; i < m_AliasLength; ++i)
            {
                if (m_Aliases[i, 0] == name)
                {
                    return m_Aliases[i, 1];
                }
            }

            return name;
        }

        public static bool DontLink(Type type)
        {
            // MONO: type.Namespace is null/empty for generic arguments

            if (type.Name == "T" || string.IsNullOrEmpty(type.Namespace) || m_Namespaces == null)
            {
                return true;
            }

            if (type.Namespace.StartsWith("Server"))
            {
                return false;
            }

            return !m_Namespaces.ContainsKey(type.Namespace);
        }
    }

    #region BodyEntry & BodyType
    public enum ModelBodyType
    {
        Invalid = -1,
        Monsters,
        Sea,
        Animals,
        Human,
        Equipment
    }

    public class BodyEntry
    {
        public Body Body { get; }
        public ModelBodyType BodyType { get; }
        public string Name { get; }

        public BodyEntry(Body body, ModelBodyType bodyType, string name)
        {
            Body = body;
            BodyType = bodyType;
            Name = name;
        }

        public override bool Equals(object obj)
        {
            BodyEntry e = (BodyEntry)obj;

            return Body == e.Body && BodyType == e.BodyType && Name == e.Name;
        }

        public override int GetHashCode()
        {
            return Body.BodyID ^ (int)BodyType ^ Name.GetHashCode();
        }
    }

    public class BodyEntrySorter : IComparer<BodyEntry>
    {
        public int Compare(BodyEntry a, BodyEntry b)
        {
            int v = a.BodyType.CompareTo(b.BodyType);

            if (v == 0)
            {
                v = a.Body.BodyID.CompareTo(b.Body.BodyID);
            }

            if (v == 0)
            {
                v = string.Compare(a.Name, b.Name, StringComparison.Ordinal);
            }

            return v;
        }
    }
    #endregion
}
