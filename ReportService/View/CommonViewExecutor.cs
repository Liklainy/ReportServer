﻿using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using PagedList;
using RazorEngine;
using RazorEngine.Configuration;
using RazorEngine.Templating;
using ReportService.Interfaces;

namespace ReportService.View
{
    public class CommonViewExecutor : IViewExecutor
    {
        public virtual string ExecuteHtml(string viewTemplate, string json)
        {
            string date = $"{DateTime.Now:dd.MM.yy HH:mm:ss}";

            TemplateServiceConfiguration templateConfig = new TemplateServiceConfiguration();
            templateConfig.DisableTempFileLocking = true;
            templateConfig.CachingProvider        = new DefaultCachingProvider(t => { });
            //templateConfig.Namespaces.Add("PagedList");
            var serv = RazorEngineService.Create(templateConfig);
            Engine.Razor = serv;
            Engine.Razor.Compile(viewTemplate, "somekey");
            JArray jObj = JArray.Parse(json);

            List<string> headers = new List<string>();
            foreach (JProperty p in JObject.Parse(jObj.First.ToString()).Properties())
                headers.Add(p.Name);

            List<List<string>> content = new List<List<string>>();
            foreach (JObject j in jObj.Children<JObject>())
            {
                List<string> prop = new List<string>();
                foreach (JProperty p in j.Properties()) prop.Add(p.Value.ToString());

                content.Add(prop);
            }
            //   var pagedList = content.ToPagedList(2,3);

            var model = new {Headers = headers, Content = content, Date = date};

            return Engine.Razor.Run("somekey", null, model);
        }

        public virtual string ExecuteTelegramView(string json, string reportName = "Отчёт")
        {
            string date = $"{DateTime.Now:dd.MM.yy HH:mm:ss}";

            JArray jObj = JArray.Parse(json);

            List<string> headers = new List<string>();
            foreach (JProperty p in JObject.Parse(jObj.First.ToString()).Properties())
                headers.Add(p.Name);

            List<List<string>> content = new List<List<string>>();
            foreach (JObject j in jObj.Children<JObject>())
            {
                List<string> prop = new List<string>();
                foreach (JProperty p in j.Properties()) prop.Add(p.Value.ToString());

                content.Add(prop);
            }

            var tmRep = $@"*{reportName}*" + Environment.NewLine;
            foreach (var prop in content)
            {
                tmRep = tmRep.Insert(tmRep.Length, Environment.NewLine + $"{prop[0]} : {prop[1]}");
            }

            return tmRep;
        }
    } //class
}
