using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using PlugINPRO.UI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PlugINPRO.Commands.VK
{
    [Transaction(TransactionMode.Manual)]
    public class HydraulicCalculateCommand : IExternalCommand
    {
        private ElementId _selectedSystemId;
        private Document _doc;
        private HashSet<ElementId> _globalCollectedFixtureIds;
        private Dictionary<ElementId, string> _fixtureTypeCache;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            _doc = uidoc.Document;

            try
            {
                _globalCollectedFixtureIds = new HashSet<ElementId>();
                _fixtureTypeCache = new Dictionary<ElementId, string>();

                Reference pipeRef = uidoc.Selection.PickObject(
                    Autodesk.Revit.UI.Selection.ObjectType.Element,
                    "ШАГ 1: Выберите любую трубу в нужной системе");

                Pipe selectedPipe = _doc.GetElement(pipeRef) as Pipe;
                if (selectedPipe == null)
                {
                    TaskDialog.Show("Ошибка", "Выбран не трубопровод");
                    return Result.Failed;
                }

                PipingSystem system = GetPipingSystem(selectedPipe);
                if (system == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось определить систему трубопровода");
                    return Result.Failed;
                }

                _selectedSystemId = system.Id;
                TaskDialog.Show("Информация", $"Выбрана система: {system.Name ?? "Без имени"}");

                Reference fixtureRef = uidoc.Selection.PickObject(
                    Autodesk.Revit.UI.Selection.ObjectType.Element,
                    "ШАГ 2: Выберите конечный санитарный прибор в этой системе");

                FamilyInstance fixture = _doc.GetElement(fixtureRef) as FamilyInstance;
                if (fixture == null)
                {
                    TaskDialog.Show("Ошибка", "Выбран не санитарный прибор");
                    return Result.Failed;
                }

                Pipe startPipe = FindConnectedPipeInSystem(fixture);
                if (startPipe == null)
                {
                    TaskDialog.Show("Ошибка",
                        "Не найдена труба у прибора в выбранной системе.\n" +
                        "Убедитесь, что прибор подключён к той же системе.");
                    return Result.Failed;
                }

                string startFixtureName = TryGetFixtureTypeName(fixture);
                if (startFixtureName != null)
                    _globalCollectedFixtureIds.Add(fixture.Id);

                List<Pipe> path = BuildPath(startPipe);

                if (path.Count == 0)
                {
                    TaskDialog.Show("Ошибка", "Путь не построен — проверьте подключение труб");
                    return Result.Failed;
                }

                List<HydraulicSection> sections = BuildSections(path, startPipe, startFixtureName);

                var vm = new HydraulicCalcViewModel
                {
                    SystemName = system.Name ?? "Без имени",
                    SystemInfo = $"Участков: {sections.Count}, суммарная длина: {Math.Round(sections.Sum(s => s.Length), 2)} м",
                    Sections = new ObservableCollection<HydraulicSection>(sections)
                };

                new HydraulicCalcWindow(vm).ShowDialog();
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка", $"{ex.Message}\n\n{ex.StackTrace}");
                return Result.Failed;
            }
        }

        private List<Pipe> BuildPath(Pipe startPipe)
        {
            List<Pipe> path = new List<Pipe>();
            HashSet<ElementId> visited = new HashSet<ElementId>();
            Pipe current = startPipe;

            while (current != null && !visited.Contains(current.Id))
            {
                visited.Add(current.Id);
                path.Add(current);

                double currentFlow = GetPipeFlow(current);
                List<Pipe> connectedPipes = GetConnectedPipesInSystem(current, visited);

                if (connectedPipes.Count == 0)
                    break;

                Pipe nextPipe = null;
                double bestFlow = currentFlow - 0.0001;

                foreach (Pipe candidate in connectedPipes)
                {
                    double candidateFlow = GetPipeFlow(candidate);
                    if (candidateFlow >= bestFlow)
                    {
                        bestFlow = candidateFlow;
                        nextPipe = candidate;
                    }
                }

                if (nextPipe != null)
                {
                    var topCandidates = connectedPipes
                        .Where(p => Math.Abs(GetPipeFlow(p) - bestFlow) < 0.0001)
                        .ToList();

                    if (topCandidates.Count > 1)
                        nextPipe = SelectByDirection(current, topCandidates);
                }

                current = nextPipe;
            }

            return path;
        }

        private List<HydraulicSection> BuildSections(List<Pipe> path, Pipe startPipe, string startFixtureName)
        {
            List<HydraulicSection> sections = new List<HydraulicSection>();
            List<Dictionary<string, int>> fixtureMaps = new List<Dictionary<string, int>>();
            List<Dictionary<string, List<int>>> fixturePipeMaps = new List<Dictionary<string, List<int>>>();
            int num = 1;
            int index = 0;
            bool isFirstSection = true;

            Dictionary<ElementId, int> pipeToSection = new Dictionary<ElementId, int>();

            while (index < path.Count)
            {
                Pipe firstPipe = path[index];
                double sectionFlow = Math.Round(GetPipeFlow(firstPipe), 5);
                double totalLength = 0;
                int startIndex = index;

                while (index < path.Count &&
                       Math.Abs(Math.Round(GetPipeFlow(path[index]), 5) - sectionFlow) < 0.00005)
                {
                    totalLength += GetPipeLength(path[index]);
                    pipeToSection[path[index].Id] = sections.Count;
                    index++;
                }

                List<int> pipeIds = new List<int>();
                for (int j = startIndex; j < index; j++)
                    pipeIds.Add(path[j].Id.IntegerValue);

                fixtureMaps.Add(new Dictionary<string, int>());
                fixturePipeMaps.Add(new Dictionary<string, List<int>>());

                sections.Add(new HydraulicSection
                {
                    Number = num++,
                    Diameter = GetPipeDiameterString(firstPipe),
                    Length = Math.Round(totalLength, 2),
                    Flow = Math.Round(sectionFlow * 28.3168, 3),
                    Velocity = "-",
                    FixtureCount = 0,
                    FixtureNames = "-",
                    PipeIds = string.Join(", ", pipeIds)
                });
            }

            for (int i = 0; i < sections.Count; i++)
            {
                if (i == 0)
                    continue;

                var pipesInSection = path.Where(p => pipeToSection[p.Id] == i).ToList();

                foreach (Pipe pipe in pipesInSection)
                {
                    var fixturesWithPipes = GetFixtureTypeNamesOnPipeWithBlockAndPipeIds(
                        pipe, pipeToSection, path);

                    foreach (var fixtureData in fixturesWithPipes)
                    {
                        string fixtureType = fixtureData.Key;
                        int pipeId = fixtureData.Value;

                        if (fixtureMaps[i].ContainsKey(fixtureType))
                            fixtureMaps[i][fixtureType]++;
                        else
                            fixtureMaps[i][fixtureType] = 1;

                        if (!fixturePipeMaps[i].ContainsKey(fixtureType))
                            fixturePipeMaps[i][fixtureType] = new List<int>();

                        if (!fixturePipeMaps[i][fixtureType].Contains(pipeId))
                            fixturePipeMaps[i][fixtureType].Add(pipeId);
                    }
                }
            }

            if (startFixtureName != null)
            {
                fixtureMaps[0][startFixtureName] = 1;
                if (!fixturePipeMaps[0].ContainsKey(startFixtureName))
                    fixturePipeMaps[0][startFixtureName] = new List<int>();

                int startPipeId = startPipe.Id.IntegerValue;
                if (!fixturePipeMaps[0][startFixtureName].Contains(startPipeId))
                    fixturePipeMaps[0][startFixtureName].Add(startPipeId);
            }

            for (int i = 1; i < fixtureMaps.Count; i++)
            {
                foreach (var kv in fixtureMaps[i - 1])
                {
                    if (fixtureMaps[i].ContainsKey(kv.Key))
                        fixtureMaps[i][kv.Key] += kv.Value;
                    else
                        fixtureMaps[i][kv.Key] = kv.Value;

                    if (fixturePipeMaps[i - 1].ContainsKey(kv.Key))
                    {
                        if (!fixturePipeMaps[i].ContainsKey(kv.Key))
                            fixturePipeMaps[i][kv.Key] = new List<int>();

                        foreach (int pipeId in fixturePipeMaps[i - 1][kv.Key])
                        {
                            if (!fixturePipeMaps[i][kv.Key].Contains(pipeId))
                                fixturePipeMaps[i][kv.Key].Add(pipeId);
                        }
                    }
                }
            }

            for (int i = 0; i < sections.Count; i++)
            {
                sections[i].FixtureCount = fixtureMaps[i].Values.Sum();

                var fixtureParts = new List<string>();
                foreach (var kv in fixtureMaps[i])
                {
                    string fixtureName = kv.Key;
                    int count = kv.Value;
                    string pipeIdsStr = "";

                    if (fixturePipeMaps[i].ContainsKey(fixtureName) && fixturePipeMaps[i][fixtureName].Count > 0)
                    {
                        pipeIdsStr = " [трубы: " + string.Join(", ", fixturePipeMaps[i][fixtureName]) + "]";
                    }

                    if (count > 1)
                        fixtureParts.Add($"{fixtureName} ({count}){pipeIdsStr}");
                    else
                        fixtureParts.Add($"{fixtureName}{pipeIdsStr}");
                }

                sections[i].FixtureNames = fixtureParts.Count > 0 ? string.Join(", ", fixtureParts) : "-";
            }

            return sections;
        }

        // =====================================================================
        // СБОР ПРИБОРОВ С ID ТРУБ (ПРАВИЛЬНЫЙ ОБХОД ФИТИНГОВ)
        // =====================================================================

        private List<KeyValuePair<string, int>> GetFixtureTypeNamesOnPipeWithBlockAndPipeIds(
            Pipe pipe,
            Dictionary<ElementId, int> pipeToSection,
            List<Pipe> path)
        {
            var result = new List<KeyValuePair<string, int>>();
            HashSet<ElementId> pathPipeIds = new HashSet<ElementId>(path.Select(p => p.Id));
            HashSet<ElementId> visited = new HashSet<ElementId>();

            int currentSection = pipeToSection[pipe.Id];

            ConnectorSet connectors = pipe.ConnectorManager?.Connectors;
            if (connectors == null) return result;

            foreach (Connector c in connectors)
            {
                if (c == null || !c.IsConnected) continue;
                foreach (Connector conn in c.AllRefs)
                {
                    if (conn?.Owner == null || conn.Owner.Id == pipe.Id) continue;

                    Element owner = conn.Owner;

                    if (owner is Pipe pathPipe && pathPipeIds.Contains(pathPipe.Id))
                    {
                        if (pathPipe.Id != pipe.Id)
                        {
                            if (GetPipeFlow(pathPipe) > GetPipeFlow(pipe))
                            {
                                // Разрешаем
                            }
                            else
                            {
                                continue;
                            }
                        }
                    }

                    if (owner is FamilyInstance fi)
                    {
                        string typeName = TryGetFixtureTypeName(fi);
                        if (typeName != null)
                        {
                            if (!_globalCollectedFixtureIds.Contains(fi.Id))
                            {
                                List<int> connectedSections = GetConnectedSectionsForFixture(fi, pathPipeIds, pipeToSection);

                                bool shouldAdd;
                                if (connectedSections.Count > 1)
                                {
                                    int targetSection = connectedSections.Max();
                                    shouldAdd = (currentSection == targetSection);
                                }
                                else if (connectedSections.Count == 1)
                                {
                                    shouldAdd = (connectedSections[0] == currentSection);
                                }
                                else
                                {
                                    shouldAdd = true;
                                }

                                if (shouldAdd)
                                {
                                    _globalCollectedFixtureIds.Add(fi.Id);
                                    if (!_fixtureTypeCache.ContainsKey(fi.Id))
                                        _fixtureTypeCache[fi.Id] = typeName;

                                    int pipeId = FindConnectedPathPipeId(fi, pathPipeIds, pipeToSection, path);
                                    if (pipeId == 0) pipeId = pipe.Id.IntegerValue;
                                    result.Add(new KeyValuePair<string, int>(typeName, pipeId));
                                }
                            }
                        }
                        else
                        {
                            visited = new HashSet<ElementId>(pathPipeIds) { pipe.Id, fi.Id };
                            CollectFixturesFromFittingWithBlockAndPipeIds(
                                fi, pipe, result, visited, pathPipeIds, pipeToSection, path);
                        }
                    }
                    else if (owner is FlexPipe flex)
                    {
                        CollectFixturesFromFlexWithPipeIds(flex, pipe.Id, result, pathPipeIds, pipeToSection, path);
                    }
                    else if (owner is Pipe branchPipe && !pathPipeIds.Contains(branchPipe.Id))
                    {
                        CollectFixturesFromBranchPipeWithBlockAndPipeIds(
                            branchPipe, pipe, result, visited, pathPipeIds, pipeToSection, path);
                    }
                }
            }

            return result;
        }

        private void CollectFixturesFromFittingWithBlockAndPipeIds(
            FamilyInstance fitting,
            Pipe currentPipe,
            List<KeyValuePair<string, int>> result,
            HashSet<ElementId> visited,
            HashSet<ElementId> pathPipeIds,
            Dictionary<ElementId, int> pipeToSection,
            List<Pipe> path)
        {
            ConnectorSet fittingConns = fitting.MEPModel?.ConnectorManager?.Connectors;
            if (fittingConns == null) return;

            int currentSection = currentPipe != null ? pipeToSection[currentPipe.Id] : -1;

            foreach (Connector fc in fittingConns)
            {
                if (fc == null || !fc.IsConnected) continue;
                foreach (Connector fconn in fc.AllRefs)
                {
                    if (fconn?.Owner == null || visited.Contains(fconn.Owner.Id)) continue;

                    Element neighbor = fconn.Owner;
                    visited.Add(neighbor.Id);

                    if (neighbor is Pipe pathPipe && pathPipeIds.Contains(pathPipe.Id))
                    {
                        if (currentPipe != null && pathPipe.Id != currentPipe.Id)
                        {
                            if (GetPipeFlow(pathPipe) > GetPipeFlow(currentPipe))
                            {
                                // Разрешаем
                            }
                            else
                            {
                                continue;
                            }
                        }
                    }

                    if (neighbor is FamilyInstance fi2)
                    {
                        string t = TryGetFixtureTypeName(fi2);
                        if (t != null)
                        {
                            List<int> connectedSections = GetConnectedSectionsForFixture(fi2, pathPipeIds, pipeToSection);

                            bool shouldAdd = false;

                            if (connectedSections.Count > 1)
                            {
                                int targetSection = connectedSections.Max();

                                if (currentSection == targetSection)
                                    shouldAdd = true;
                            }
                            else if (connectedSections.Count == 1 && connectedSections[0] == currentSection)
                            {
                                shouldAdd = true;
                            }

                            if (shouldAdd)
                            {
                                if (!_globalCollectedFixtureIds.Contains(fi2.Id))
                                {
                                    _globalCollectedFixtureIds.Add(fi2.Id);
                                    if (!_fixtureTypeCache.ContainsKey(fi2.Id))
                                        _fixtureTypeCache[fi2.Id] = t;

                                    int pipeId = FindConnectedPathPipeId(fi2, pathPipeIds, pipeToSection, path);
                                    if (pipeId == 0 && currentPipe != null) pipeId = currentPipe.Id.IntegerValue;
                                    result.Add(new KeyValuePair<string, int>(t, pipeId));
                                }
                            }
                            continue;
                        }

                        CollectFixturesFromFittingWithBlockAndPipeIds(
                            fi2, currentPipe, result, visited, pathPipeIds, pipeToSection, path);
                    }
                    else if (neighbor is Pipe branchPipe && !pathPipeIds.Contains(branchPipe.Id))
                    {
                        CollectFixturesFromBranchPipeWithBlockAndPipeIds(
                            branchPipe, currentPipe, result, visited, pathPipeIds, pipeToSection, path);
                    }
                    else if (neighbor is FlexPipe flex)
                    {
                        CollectFixturesFromFlexWithPipeIds(flex, currentPipe?.Id ?? fitting.Id, result, pathPipeIds, pipeToSection, path);
                    }
                }
            }
        }

        private void CollectFixturesFromBranchPipeWithBlockAndPipeIds(
            Pipe branchPipe,
            Pipe currentPipe,
            List<KeyValuePair<string, int>> result,
            HashSet<ElementId> visited,
            HashSet<ElementId> pathPipeIds,
            Dictionary<ElementId, int> pipeToSection,
            List<Pipe> path)
        {
            ConnectorSet conns = branchPipe.ConnectorManager?.Connectors;
            if (conns == null) return;

            int currentSection = currentPipe != null ? pipeToSection[currentPipe.Id] : -1;

            foreach (Connector c in conns)
            {
                if (c == null || !c.IsConnected) continue;
                foreach (Connector conn in c.AllRefs)
                {
                    if (conn?.Owner == null || conn.Owner.Id == branchPipe.Id || (currentPipe != null && conn.Owner.Id == currentPipe.Id)) continue;
                    if (visited.Contains(conn.Owner.Id)) continue;

                    Element neighbor = conn.Owner;
                    visited.Add(neighbor.Id);

                    if (neighbor is Pipe pathPipe && pathPipeIds.Contains(pathPipe.Id))
                    {
                        if (currentPipe != null && pathPipe.Id != currentPipe.Id)
                        {
                            if (GetPipeFlow(pathPipe) > GetPipeFlow(currentPipe))
                            {
                                // Разрешаем
                            }
                            else
                            {
                                continue;
                            }
                        }
                    }

                    if (neighbor is FamilyInstance fi)
                    {
                        string t = TryGetFixtureTypeName(fi);
                        if (t != null)
                        {
                            List<int> connectedSections = GetConnectedSectionsForFixture(fi, pathPipeIds, pipeToSection);

                            bool shouldAdd = false;

                            if (connectedSections.Count > 1)
                            {
                                int targetSection = connectedSections.Max();

                                if (currentSection == targetSection)
                                    shouldAdd = true;
                            }
                            else if (connectedSections.Count == 1 && connectedSections[0] == currentSection)
                            {
                                shouldAdd = true;
                            }

                            if (shouldAdd)
                            {
                                if (!_globalCollectedFixtureIds.Contains(fi.Id))
                                {
                                    _globalCollectedFixtureIds.Add(fi.Id);
                                    if (!_fixtureTypeCache.ContainsKey(fi.Id))
                                        _fixtureTypeCache[fi.Id] = t;

                                    int pipeId = FindConnectedPathPipeId(fi, pathPipeIds, pipeToSection, path);
                                    if (pipeId == 0 && currentPipe != null) pipeId = currentPipe.Id.IntegerValue;
                                    result.Add(new KeyValuePair<string, int>(t, pipeId));
                                }
                            }
                            continue;
                        }
                        else
                        {
                            CollectFixturesFromFittingWithBlockAndPipeIds(
                                fi, currentPipe, result, visited, pathPipeIds, pipeToSection, path);
                        }
                    }
                    else if (neighbor is FlexPipe flex)
                    {
                        CollectFixturesFromFlexWithPipeIds(flex, branchPipe.Id, result, pathPipeIds, pipeToSection, path);
                    }
                    else if (neighbor is Pipe nextBranch && !pathPipeIds.Contains(nextBranch.Id))
                    {
                        CollectFixturesFromBranchPipeWithBlockAndPipeIds(
                            nextBranch, currentPipe, result, visited, pathPipeIds, pipeToSection, path);
                    }
                }
            }
        }

        private void CollectFixturesFromFlexWithPipeIds(
            FlexPipe flex,
            ElementId fromId,
            List<KeyValuePair<string, int>> result,
            HashSet<ElementId> pathPipeIds,
            Dictionary<ElementId, int> pipeToSection,
            List<Pipe> path)
        {
            ConnectorSet flexConns = flex.ConnectorManager?.Connectors;
            if (flexConns == null) return;

            foreach (Connector fc in flexConns)
            {
                if (fc == null || !fc.IsConnected) continue;
                foreach (Connector fconn in fc.AllRefs)
                {
                    if (fconn?.Owner == null || fconn.Owner.Id == flex.Id || fconn.Owner.Id == fromId) continue;

                    if (fconn.Owner is FamilyInstance fi2)
                    {
                        string t = TryGetFixtureTypeName(fi2);
                        if (t != null)
                        {
                            if (!_globalCollectedFixtureIds.Contains(fi2.Id))
                            {
                                List<int> connectedSections = GetConnectedSectionsForFixture(fi2, pathPipeIds, pipeToSection);

                                int currentSection = -1;
                                if (pipeToSection.TryGetValue(fromId, out int sec))
                                    currentSection = sec;

                                bool shouldAdd = false;
                                if (connectedSections.Count > 1)
                                {
                                    int targetSection = connectedSections.Max();
                                    if (currentSection == targetSection)
                                        shouldAdd = true;
                                }
                                else if (connectedSections.Count == 1 && currentSection == connectedSections[0])
                                {
                                    shouldAdd = true;
                                }
                                else if (connectedSections.Count == 0)
                                {
                                    shouldAdd = true;
                                }

                                if (shouldAdd)
                                {
                                    _globalCollectedFixtureIds.Add(fi2.Id);
                                    if (!_fixtureTypeCache.ContainsKey(fi2.Id))
                                        _fixtureTypeCache[fi2.Id] = t;

                                    int pipeId = FindConnectedPathPipeId(fi2, pathPipeIds, pipeToSection, path);
                                    result.Add(new KeyValuePair<string, int>(t, pipeId));
                                }
                            }
                        }
                        else
                        {
                            CollectFixturesFromFittingWithBlockAndPipeIds(
                                fi2, null, result, new HashSet<ElementId>(), pathPipeIds, pipeToSection, path);
                        }
                    }
                    else if (fconn.Owner is Pipe pipe)
                    {
                        if (!pathPipeIds.Contains(pipe.Id))
                        {
                            CollectFixturesFromBranchPipeWithBlockAndPipeIds(
                                pipe, null, result, new HashSet<ElementId>(), pathPipeIds, pipeToSection, path);
                        }
                    }
                }
            }
        }

        // =====================================================================
        // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
        // =====================================================================

        private int FindConnectedPathPipeId(
            FamilyInstance fixture,
            HashSet<ElementId> pathPipeIds,
            Dictionary<ElementId, int> pipeToSection,
            List<Pipe> path)
        {
            HashSet<ElementId> visited = new HashSet<ElementId>();
            Stack<Element> stack = new Stack<Element>();
            List<int> foundPipeIds = new List<int>();

            if (fixture.MEPModel == null) return 0;

            foreach (Connector c in fixture.MEPModel.ConnectorManager.Connectors)
            {
                if (c == null || !c.IsConnected) continue;
                foreach (Connector conn in c.AllRefs)
                {
                    if (conn?.Owner != null)
                        stack.Push(conn.Owner);
                }
            }

            while (stack.Count > 0)
            {
                Element current = stack.Pop();
                if (current == null || visited.Contains(current.Id)) continue;
                visited.Add(current.Id);

                if (current is Pipe pipe && pathPipeIds.Contains(pipe.Id))
                {
                    if (!foundPipeIds.Contains(pipe.Id.IntegerValue))
                        foundPipeIds.Add(pipe.Id.IntegerValue);
                    continue;
                }

                ConnectorSet connectors = null;
                if (current is Pipe p)
                    connectors = p.ConnectorManager?.Connectors;
                else if (current is FlexPipe fp)
                    connectors = fp.ConnectorManager?.Connectors;
                else if (current is FamilyInstance fi && fi.MEPModel != null)
                    connectors = fi.MEPModel.ConnectorManager?.Connectors;

                if (connectors == null) continue;

                foreach (Connector c in connectors)
                {
                    if (c == null || !c.IsConnected) continue;
                    foreach (Connector conn in c.AllRefs)
                    {
                        if (conn?.Owner == null || visited.Contains(conn.Owner.Id)) continue;

                        if (conn.Owner is Pipe pipe2 && pathPipeIds.Contains(pipe2.Id))
                        {
                            if (!foundPipeIds.Contains(pipe2.Id.IntegerValue))
                                foundPipeIds.Add(pipe2.Id.IntegerValue);
                            continue;
                        }

                        stack.Push(conn.Owner);
                    }
                }
            }

            if (foundPipeIds.Count == 0) return 0;
            if (foundPipeIds.Count == 1) return foundPipeIds[0];

            int bestPipeId = 0;
            double minFlow = double.MaxValue;

            foreach (int pipeId in foundPipeIds)
            {
                var pipe = path.FirstOrDefault(p => p.Id.IntegerValue == pipeId);
                if (pipe != null)
                {
                    double flow = GetPipeFlow(pipe);
                    if (flow < minFlow)
                    {
                        minFlow = flow;
                        bestPipeId = pipeId;
                    }
                }
            }

            return bestPipeId;
        }

        private List<int> GetConnectedSectionsForFixture(
            FamilyInstance fixture,
            HashSet<ElementId> pathPipeIds,
            Dictionary<ElementId, int> pipeToSection)
        {
            List<int> sections = new List<int>();
            HashSet<ElementId> visited = new HashSet<ElementId>();
            Stack<Element> stack = new Stack<Element>();

            if (fixture.MEPModel == null) return sections;

            foreach (Connector c in fixture.MEPModel.ConnectorManager.Connectors)
            {
                if (c == null || !c.IsConnected) continue;
                foreach (Connector conn in c.AllRefs)
                {
                    if (conn?.Owner != null)
                        stack.Push(conn.Owner);
                }
            }

            while (stack.Count > 0)
            {
                Element current = stack.Pop();
                if (current == null || visited.Contains(current.Id)) continue;
                visited.Add(current.Id);

                if (current is Pipe pipe && pathPipeIds.Contains(pipe.Id))
                {
                    if (pipeToSection.TryGetValue(pipe.Id, out int sectionIdx))
                    {
                        if (!sections.Contains(sectionIdx))
                            sections.Add(sectionIdx);
                    }
                    continue;
                }

                ConnectorSet connectors = null;
                if (current is Pipe p) connectors = p.ConnectorManager?.Connectors;
                else if (current is FlexPipe fp) connectors = fp.ConnectorManager?.Connectors;
                else if (current is FamilyInstance fi && fi.MEPModel != null)
                    connectors = fi.MEPModel.ConnectorManager?.Connectors;

                if (connectors == null) continue;

                foreach (Connector c in connectors)
                {
                    if (c == null || !c.IsConnected) continue;
                    foreach (Connector conn in c.AllRefs)
                    {
                        if (conn?.Owner != null && !visited.Contains(conn.Owner.Id))
                            stack.Push(conn.Owner);
                    }
                }
            }

            return sections;
        }

        private string TryGetFixtureTypeName(FamilyInstance fi)
        {
            if (fi == null || fi.MEPModel == null) return null;

            if (_fixtureTypeCache.ContainsKey(fi.Id))
                return _fixtureTypeCache[fi.Id];

            BuiltInCategory cat = (BuiltInCategory)fi.Category?.Id?.IntegerValue;
            if (cat == BuiltInCategory.OST_PipeFitting ||
                cat == BuiltInCategory.OST_PipeAccessory)
                return null;

            ElementType elementType = _doc.GetElement(fi.GetTypeId()) as ElementType;
            string typeName = elementType?.Name;

            return string.IsNullOrWhiteSpace(typeName) ? null : typeName;
        }

        // =====================================================================
        // GetConnectedPipesInSystem С РЕКУРСИВНЫМ ОБХОДОМ ЦЕПОЧЕК ФИТИНГОВ
        // =====================================================================

        private List<Pipe> GetConnectedPipesInSystem(Pipe pipe, HashSet<ElementId> visited)
        {
            List<Pipe> result = new List<Pipe>();
            ConnectorSet connectors = pipe.ConnectorManager?.Connectors;
            if (connectors == null) return result;

            foreach (Connector c in connectors)
            {
                if (c == null || !c.IsConnected) continue;

                foreach (Connector conn in c.AllRefs)
                {
                    if (conn?.Owner == null || conn.Owner.Id == pipe.Id) continue;

                    Element owner = conn.Owner;

                    if (owner is Pipe directPipe)
                    {
                        TryAddPipe(directPipe, visited, result);
                    }
                    else if (owner is FamilyInstance fitting && fitting.MEPModel != null)
                    {
                        CollectPipesThroughFittingChain(
                            fitting, pipe.Id, new HashSet<ElementId> { pipe.Id }, visited, result);
                    }
                }
            }

            return result;
        }

        private void CollectPipesThroughFittingChain(
            FamilyInstance fitting,
            ElementId fromId,
            HashSet<ElementId> localVisited,
            HashSet<ElementId> visited,
            List<Pipe> result)
        {
            if (localVisited.Contains(fitting.Id)) return;
            localVisited.Add(fitting.Id);

            ConnectorSet fittingConnectors = fitting.MEPModel?.ConnectorManager?.Connectors;
            if (fittingConnectors == null) return;

            foreach (Connector fc in fittingConnectors)
            {
                if (fc == null || !fc.IsConnected) continue;
                foreach (Connector fconn in fc.AllRefs)
                {
                    if (fconn?.Owner == null || fconn.Owner.Id == fitting.Id || fconn.Owner.Id == fromId) continue;
                    if (localVisited.Contains(fconn.Owner.Id)) continue;

                    if (fconn.Owner is Pipe pipeFromFitting)
                    {
                        TryAddPipe(pipeFromFitting, visited, result);
                    }
                    else if (fconn.Owner is FamilyInstance nextFitting && nextFitting.MEPModel != null)
                    {
                        CollectPipesThroughFittingChain(nextFitting, fitting.Id, localVisited, visited, result);
                    }
                }
            }
        }

        private void TryAddPipe(Pipe candidate, HashSet<ElementId> visited, List<Pipe> result)
        {
            if (candidate == null) return;
            if (visited.Contains(candidate.Id)) return;
            if (candidate.MEPSystem?.Id != _selectedSystemId) return;
            if (result.Any(p => p.Id == candidate.Id)) return;

            result.Add(candidate);
        }

        private Pipe FindConnectedPipeInSystem(FamilyInstance fixture)
        {
            if (fixture.MEPModel == null) return null;

            ConnectorSet connectors = fixture.MEPModel.ConnectorManager?.Connectors;
            if (connectors == null) return null;

            foreach (Connector c in connectors)
            {
                if (c == null || !c.IsConnected) continue;
                foreach (Connector conn in c.AllRefs)
                {
                    if (conn?.Owner == null) continue;

                    if (conn.Owner is Pipe pipe && pipe.MEPSystem?.Id == _selectedSystemId)
                        return pipe;

                    if (conn.Owner is FlexPipe flex)
                    {
                        ConnectorSet flexConnectors = flex.ConnectorManager?.Connectors;
                        if (flexConnectors == null) continue;

                        foreach (Connector fc in flexConnectors)
                        {
                            if (fc == null || !fc.IsConnected) continue;
                            foreach (Connector fconn in fc.AllRefs)
                            {
                                if (fconn?.Owner is Pipe pipeFromFlex &&
                                    pipeFromFlex.MEPSystem?.Id == _selectedSystemId)
                                    return pipeFromFlex;
                            }
                        }
                    }

                    if (conn.Owner is FamilyInstance fitting && fitting.MEPModel != null)
                    {
                        ConnectorSet fittingConnectors = fitting.MEPModel.ConnectorManager?.Connectors;
                        if (fittingConnectors == null) continue;

                        foreach (Connector fc in fittingConnectors)
                        {
                            if (fc == null || !fc.IsConnected) continue;
                            foreach (Connector fconn in fc.AllRefs)
                            {
                                if (fconn?.Owner is Pipe pipeFromFitting &&
                                    pipeFromFitting.MEPSystem?.Id == _selectedSystemId)
                                    return pipeFromFitting;
                            }
                        }
                    }
                }
            }

            return null;
        }

        private Pipe SelectByDirection(Pipe current, List<Pipe> candidates)
        {
            XYZ currentDir = GetPipeDirection(current);
            if (currentDir == null)
                return candidates.OrderBy(p => p.Id.IntegerValue).First();

            Pipe best = null;
            double minAngle = double.MaxValue;

            foreach (Pipe candidate in candidates)
            {
                XYZ candidateDir = GetPipeDirection(candidate);
                if (candidateDir == null) continue;

                double angle = Math.Min(
                    currentDir.AngleTo(candidateDir),
                    currentDir.AngleTo(candidateDir.Negate())
                ) * (180.0 / Math.PI);

                if (angle < minAngle - 0.001)
                {
                    minAngle = angle;
                    best = candidate;
                }
                else if (Math.Abs(angle - minAngle) < 0.001)
                {
                    if (best == null || candidate.Id.IntegerValue < best.Id.IntegerValue)
                        best = candidate;
                }
            }

            return best ?? candidates.OrderBy(p => p.Id.IntegerValue).First();
        }

        private XYZ GetPipeDirection(Pipe pipe)
        {
            LocationCurve loc = pipe.Location as LocationCurve;
            if (loc == null) return null;
            XYZ start = loc.Curve.GetEndPoint(0);
            XYZ end = loc.Curve.GetEndPoint(1);
            XYZ dir = end - start;
            return dir.GetLength() > 1e-6 ? dir.Normalize() : null;
        }

        private PipingSystem GetPipingSystem(Pipe pipe)
        {
            if (pipe.MEPSystem == null) return null;
            return _doc.GetElement(pipe.MEPSystem.Id) as PipingSystem;
        }

        private double GetPipeFlow(Pipe pipe) =>
            pipe.get_Parameter(BuiltInParameter.RBS_PIPE_FLOW_PARAM)?.AsDouble() ?? 0;

        private double GetPipeLength(Pipe pipe) =>
            (pipe.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble() ?? 0) * 0.3048;

        private string GetPipeDiameterString(Pipe pipe)
        {
            double mm = (pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.AsDouble() ?? 0) * 304.8;
            return mm > 0 ? $"DN{Math.Round(mm)}" : "DN?";
        }
    }
}