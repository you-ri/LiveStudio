// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;
using Lilium.RemoteControl.RestApi;
using Lilium.RemoteControl.Server;

namespace Lilium.RemoteControl.UI
{
    /// <summary>
    /// REST API handler for the Remote Control UI.
    /// Handles requests to /ui/*.
    ///
    /// Serves a side menu merged from the host's primary <see cref="UIDefinition"/> plus any
    /// definitions added at runtime through <see cref="UIDefinitionRegistry"/>. Because a single
    /// handler owns <c>/live/ui/sidemenu</c>, dynamic pages must be merged here rather than by
    /// registering a second handler.
    /// </summary>
    public class UIHandler : BaseRemoteControlApiHandler
    {
        private readonly UIDefinition _definition;
        private readonly string _appTitle;

        // Selectors/factories registered per definition, so a removed definition unregisters
        // exactly its own exposed objects. The primary definition is keyed too.
        private readonly Dictionary<UIDefinition, DefinitionRegistration> _registrations =
            new Dictionary<UIDefinition, DefinitionRegistration>();

        private sealed class DefinitionRegistration
        {
            public readonly List<LiveObjectHandle> selectors = new List<LiveObjectHandle>();
            public readonly List<LiveObjectHandle> factories = new List<LiveObjectHandle>();
        }

        public UIHandler(RemoteControlServerCore server, UIDefinition definition) : base(server)
        {
            _definition = definition;
            // UnityEngine APIs are main-thread only; cache here at construction time.
            _appTitle = Application.productName ?? string.Empty;

            if (_definition != null) _RegisterDefinition(_definition);

            // Merge definitions added before this handler existed, then stay in sync.
            var additional = UIDefinitionRegistry.definitions;
            for (int i = 0; i < additional.Length; i++)
            {
                _RegisterDefinition(additional[i]);
            }
            UIDefinitionRegistry.onChanged += _OnRegistryChanged;
        }

        // Runs on the thread that mutated the registry (main thread). Registers/unregisters the
        // definition's selectors and tells connected clients to refetch the side menu.
        private void _OnRegistryChanged(UIDefinition definition, bool added)
        {
            if (definition == null || definition == _definition) return;

            if (added) _RegisterDefinition(definition);
            else _UnregisterDefinition(definition);

            // Recorded, not sent: the client notices it while polling /live/changes and refetches
            // /live/ui/sidemenu (consistent with how the types tables are published).
            LiveChangeLog.Record(LiveChangeLog.kUiId);
        }

        // Registers the selector/factory exposed objects for one definition's menu items.
        private void _RegisterDefinition(UIDefinition definition)
        {
            if (definition == null || definition.menuItems == null) return;
            if (_registrations.ContainsKey(definition)) return;

            var registration = new DefinitionRegistration();

            for (int i = 0; i < definition.menuItems.Count; i++)
            {
                var item = definition.menuItems[i];

                // CategoryPage / NavigatePage / ScenePage からセレクタ・ファクトリを取得
                IObjectSelector selector = null;
                IObjectFactory factory = null;

                var categoryPage = item.page as CategoryPage;
                if (categoryPage != null)
                {
                    selector = categoryPage.selector;
                    factory = categoryPage.factory;
                }

                var scenePage = item.page as ScenePage;
                if (scenePage != null)
                {
                    factory = scenePage.factory;
                }

                var navigatePage = item.page as NavigatePage;
                if (navigatePage != null)
                {
                    selector = navigatePage.selector;
                }

                // セレクタの登録
                if (selector != null)
                {
                    var selectorId = $"ui.selector.{item.id}";
                    var selectorLiveClass = LiveClass.Find(typeof(ObjectSelectorBase));
                    if (selectorLiveClass != null)
                    {
                        var liveObject = LiveObjectRegistry.GetOrCreate(selectorId, selectorLiveClass, selector);
                        registration.selectors.Add(liveObject);
                    }
                }

                // ファクトリの登録（CategoryPage / ScenePage）
                if (factory != null)
                {
                    if (factory is ObjectFactoryBase factoryBase)
                    {
                        var container = _context?.objectContainer;
                        factoryBase.Initialize(container);
                    }

                    factory.RegisterPrefabs();

                    var factoryId = $"ui.factory.{item.id}";
                    var factoryLiveClass = LiveClass.Find(typeof(ObjectFactoryBase));
                    if (factoryLiveClass != null)
                    {
                        var factoryLiveObject = LiveObjectRegistry.GetOrCreate(factoryId, factoryLiveClass, factory);
                        registration.factories.Add(factoryLiveObject);
                    }
                }
            }

            _registrations[definition] = registration;
        }

        private void _UnregisterDefinition(UIDefinition definition)
        {
            if (definition == null) return;
            if (!_registrations.TryGetValue(definition, out var registration)) return;

            for (int i = 0; i < registration.selectors.Count; i++)
            {
                registration.selectors[i].Unregister();
            }
            for (int i = 0; i < registration.factories.Count; i++)
            {
                registration.factories[i].Unregister();
            }
            _registrations.Remove(definition);
        }

        public override void Cleanup()
        {
            UIDefinitionRegistry.onChanged -= _OnRegistryChanged;

            // Snapshot the keys because _UnregisterDefinition mutates the dictionary.
            var keys = new List<UIDefinition>(_registrations.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                _UnregisterDefinition(keys[i]);
            }
        }

        public override bool CanHandle(HttpListenerRequest request)
        {
            var path = request.Url.AbsolutePath.ToLower();
            return path.StartsWith("/live/ui/");
        }

        protected override bool SupportsGet() => true;

        protected override Task HandleGetRequest(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath.ToLower();

            if (path == "/live/ui/sidemenu")
            {
                return HandleGetSideMenu(context);
            }

            if (path == "/live/ui/info")
            {
                return HandleGetInfo(context);
            }

            return SendNotFound(context);
        }

        private Task HandleGetInfo(HttpListenerContext context)
        {
            var response = new InfoResponse { title = _appTitle };
            var json = JsonUtility.ToJson(response);
            return WriteResponse(200, context.Response, json);
        }

        private Task HandleGetSideMenu(HttpListenerContext context)
        {
            var response = new SideMenuResponse { menuItems = new List<SideMenuItemResponse>() };

            _AppendMenuItems(response.menuItems, _definition);

            // Runtime-added definitions (e.g. the embedded capture page). Read the immutable
            // snapshot so a concurrent Add/Remove on the main thread cannot tear this loop.
            var additional = UIDefinitionRegistry.definitions;
            for (int i = 0; i < additional.Length; i++)
            {
                _AppendMenuItems(response.menuItems, additional[i]);
            }

            var json = JsonUtility.ToJson(response);
            return WriteResponse(200, context.Response, json);
        }

        private static void _AppendMenuItems(List<SideMenuItemResponse> sink, UIDefinition definition)
        {
            if (definition == null || definition.menuItems == null) return;

            for (int i = 0; i < definition.menuItems.Count; i++)
            {
                var item = definition.menuItems[i];
                var categoryPage = item.page as CategoryPage;
                var scenePage = item.page as ScenePage;
                var navigatePage = item.page as NavigatePage;

                // ページタイプの判定
                string pageType;
                string selectorObjectId;
                string factoryObjectId;

                if (navigatePage != null)
                {
                    pageType = "navigate";
                    selectorObjectId = navigatePage.selector != null ? $"ui.selector.{item.id}" : "";
                    factoryObjectId = "";
                }
                else if (scenePage != null)
                {
                    pageType = "scene";
                    selectorObjectId = "";
                    factoryObjectId = scenePage.factory != null ? $"ui.factory.{item.id}" : "";
                }
                else if (categoryPage != null)
                {
                    pageType = "category";
                    selectorObjectId = categoryPage.selector != null ? $"ui.selector.{item.id}" : "";
                    factoryObjectId = categoryPage.factory != null ? $"ui.factory.{item.id}" : "";
                }
                else
                {
                    pageType = "";
                    selectorObjectId = "";
                    factoryObjectId = "";
                }

                sink.Add(new SideMenuItemResponse
                {
                    id = item.id,
                    icon = item.icon,
                    label = LocalizationSystem.Translate(item.label),
                    pageType = pageType,
                    selectorObjectId = selectorObjectId,
                    factoryObjectId = factoryObjectId,
                    position = item.position == MenuItemPosition.Bottom ? "bottom" : "main",
                    order = item.order,
                    accessLevel = (int)item.accessLevel,
                });
            }
        }

        [System.Serializable]
        private class SideMenuResponse
        {
            public List<SideMenuItemResponse> menuItems;
        }

        [System.Serializable]
        private class InfoResponse
        {
            public string title;
        }

        [System.Serializable]
        private class SideMenuItemResponse
        {
            public string id;
            public string icon;
            public string label;
            public string pageType;
            public string selectorObjectId;
            public string factoryObjectId;
            public string position;
            public int order;
            public int accessLevel;
        }
    }
}
