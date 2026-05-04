// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;
using Lilium.RemoteControl.RestApi;
using Lilium.RemoteControl.Server;

namespace Lilium.RemoteControl.WebUI
{
    /// <summary>
    /// WebUI関連のREST APIハンドラー。
    /// /webui/* へのリクエストを処理する。
    /// </summary>
    public class WebUIHandler : BaseRemoteControlApiHandler
    {
        private readonly WebUIDefinition _definition;
        private readonly List<ExposedObject> _selectorExposedObjects = new List<ExposedObject>();
        private readonly List<ExposedObject> _factoryExposedObjects = new List<ExposedObject>();

        public WebUIHandler(RemoteControlServerCore server, WebUIDefinition definition) : base(server)
        {
            _definition = definition;
            _RegisterSelectors();
        }

        private void _RegisterSelectors()
        {
            if (_definition == null || _definition.menuItems == null) return;

            for (int i = 0; i < _definition.menuItems.Count; i++)
            {
                var item = _definition.menuItems[i];

                // CategoryPage または NavigatePage からセレクタを取得
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
                    var selectorId = $"webui.selector.{item.id}";
                    var selectorExposedClass = ExposedClass.Find(typeof(ObjectSelectorBase));
                    if (selectorExposedClass != null)
                    {
                        var exposedObject = ExposedObjectRegistry.GetOrCreate(selectorId, selectorExposedClass, selector);
                        _selectorExposedObjects.Add(exposedObject);
                    }
                }

                // ファクトリの登録（CategoryPageのみ）
                if (factory != null)
                {
                    if (factory is ObjectFactoryBase factoryBase)
                    {
                        var container = _context?.objectContainer;
                        factoryBase.Initialize(container);
                    }

                    factory.RegisterPrefabs();

                    var factoryId = $"webui.factory.{item.id}";
                    var factoryExposedClass = ExposedClass.Find(typeof(ObjectFactoryBase));
                    if (factoryExposedClass != null)
                    {
                        var factoryExposedObject = ExposedObjectRegistry.GetOrCreate(factoryId, factoryExposedClass, factory);
                        _factoryExposedObjects.Add(factoryExposedObject);
                    }
                }
            }
        }

        public override void Cleanup()
        {
            for (int i = 0; i < _selectorExposedObjects.Count; i++)
            {
                _selectorExposedObjects[i].Unregister();
            }
            _selectorExposedObjects.Clear();

            for (int i = 0; i < _factoryExposedObjects.Count; i++)
            {
                _factoryExposedObjects[i].Unregister();
            }
            _factoryExposedObjects.Clear();
        }

        public override bool CanHandle(HttpListenerRequest request)
        {
            var path = request.Url.AbsolutePath.ToLower();
            return path.StartsWith("/webui/");
        }

        protected override bool SupportsGet() => true;

        protected override Task HandleGetRequest(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath.ToLower();

            if (path == "/webui/sidemenu")
            {
                return HandleGetSideMenu(context);
            }

            return SendNotFound(context);
        }

        private Task HandleGetSideMenu(HttpListenerContext context)
        {
            if (_definition == null)
            {
                return WriteResponse(200, context.Response, "{\"menuItems\":[]}");
            }

            var response = new SideMenuResponse();
            response.menuItems = new List<SideMenuItemResponse>(_definition.menuItems.Count);

            for (int i = 0; i < _definition.menuItems.Count; i++)
            {
                var item = _definition.menuItems[i];
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
                    selectorObjectId = navigatePage.selector != null ? $"webui.selector.{item.id}" : "";
                    factoryObjectId = "";
                }
                else if (scenePage != null)
                {
                    pageType = "scene";
                    selectorObjectId = "";
                    factoryObjectId = scenePage.factory != null ? $"webui.factory.{item.id}" : "";
                }
                else if (categoryPage != null)
                {
                    pageType = "category";
                    selectorObjectId = categoryPage.selector != null ? $"webui.selector.{item.id}" : "";
                    factoryObjectId = categoryPage.factory != null ? $"webui.factory.{item.id}" : "";
                }
                else
                {
                    pageType = "";
                    selectorObjectId = "";
                    factoryObjectId = "";
                }

                response.menuItems.Add(new SideMenuItemResponse
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

            var json = JsonUtility.ToJson(response);
            return WriteResponse(200, context.Response, json);
        }

        [System.Serializable]
        private class SideMenuResponse
        {
            public List<SideMenuItemResponse> menuItems;
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
