﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using QlikView.Qvx.QvxLibrary;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Dynamic;
using System.Net;
using System.Configuration;
using System.Text.RegularExpressions;

namespace GenericRestConnector
{
    public class Connection : QvxConnection
    {
        RESTHelper helper;
        Int64 recordsLoaded;
        String liveTable;
        public String session;

        public override void Init()
        {
            //Debugger.Launch();     
            if (helper == null && MParameters != null)
            {             
                helper = new RESTHelper(MParameters);
            }
        }

        private IEnumerable<QvxDataRow> GetData()
        {
            //Debugger.Launch();
            dynamic data;
            recordsLoaded = 0;

            //Get a reference to the QvxTable from MTables
            QvxTable qTable = FindTable(liveTable, MTables);
            helper.Prep();
            bool isFromCache = helper.cacheEndpointMap.ContainsKey(helper.ActiveTable.endpoint.ToString());
            while (helper.IsMore)
            {
                if (isFromCache)
                {
                    String cachedTable = helper.cacheEndpointMap[helper.ActiveTable.endpoint.ToString()];
                    if (cachedTable != liveTable)
                    {
                        data = helper.getCachedData(cachedTable, helper.pageInfo.CurrentPage);
                    }
                    else
                    {
                        data = helper.GetJSON();
                    }
                }
                else{
                    data = helper.GetJSON();
                }
                if (data == null)
                {
                    QvxLog.Log(QvxLogFacility.Application, QvxLogSeverity.Notice, "End of data (" + liveTable + ")");
                    break;
                }
                if (data.GetType().Name == "JArray")
                {
                    if(((Newtonsoft.Json.Linq.JArray)(data)).Count==0){
                        QvxLog.Log(QvxLogFacility.Application, QvxLogSeverity.Notice, "End of data (" + liveTable + ")");
                        break;
                    }
                }
                if (helper.tableCacheList.IndexOf(liveTable) != -1)
                {
                    if (!helper.cacheEndpointMap.ContainsKey(helper.ActiveTable.endpoint.ToString()))
                    {
                        helper.cacheEndpointMap.Add(helper.ActiveTable.endpoint.ToString(), liveTable);
                    }
                    helper.cacheTable(liveTable, helper.pageInfo.CurrentPage, data);
                }
                //if we have a child link configured
                if (helper.ActiveTable.has_link_to_child != null && helper.ActiveTable.has_link_to_child==true) 
                {
                    dynamic childData = data;
                    if(helper.ActiveTable.data_element_override==null || helper.ActiveTable.data_element_override.ToString()==""){
                        QvxLog.Log(QvxLogFacility.Application, QvxLogSeverity.Error, "Data Element Override has not been set for Table - "+ helper.ActiveTable.qName);
                    }
                    else
                    {
                        String backupUrl = helper.ActiveUrl;
                        String childDataElementParam = helper.ActiveTable.data_element_override.ToString();
                        List<String> childDataElement = childDataElementParam.Split(new String[] { "." }, StringSplitOptions.RemoveEmptyEntries).ToList();
                        String childDataUrlElement = childDataElement.Last();
                        childDataElement.Remove(childDataElement.Last());
                        foreach (String elem in childDataElement)
                        {
                            childData = childData[elem];
                        }
                        foreach (dynamic child in childData)
                        {
                            helper.ActiveUrl = ((dynamic)child)[childDataUrlElement];
                            childData = helper.GetJSON();

                            if (helper.ActiveTable.child_data_element != null && helper.ActiveTable.child_data_element.ToString() != "")
                            {
                                String childDataElemParam = helper.ActiveTable.child_data_element.ToString();
                                List<String> childDataElem = childDataElemParam.Split(new String[] { "." }, StringSplitOptions.RemoveEmptyEntries).ToList();
                                foreach (String elem in childDataElem)
                                {
                                    childData = childData[elem];
                                }
                            }
                            Boolean dataIsArray;
                            try
                            {
                                dynamic check = ((Newtonsoft.Json.Linq.JArray)(childData)).Type;    //if this works then the data is an array
                                dataIsArray = true;
                            }
                            catch (Exception ex)
                            {
                                dataIsArray = false;
                            }
                            if (dataIsArray)
                            {
                                if (((Newtonsoft.Json.Linq.JArray)(childData)).Count == 0)
                                {
                                    helper.IsMore = false;
                                    break;
                                }
                                foreach (dynamic row in childData)
                                {
                                    if (recordsLoaded < helper.pageInfo.LoadLimit)
                                    {
                                        yield return InsertRow(row, qTable, child);
                                        recordsLoaded++;
                                    }
                                    else
                                    {
                                        helper.IsMore = false;
                                        break;
                                    }
                                }
                            }
                            else
                            {
                                if (recordsLoaded < helper.pageInfo.LoadLimit)
                                {
                                    yield return InsertRow(childData, qTable, child);
                                }
                                else
                                {
                                    helper.IsMore = false;
                                    break;
                                }
                            }
                            
                        }
                        helper.ActiveUrl = backupUrl;
                    }
                    
                }
                else
                {
                    if (!String.IsNullOrEmpty(helper.DataElement))
                    {
                        //Debugger.Launch();
                        List<String> dataElemPath = helper.DataElement.Split(new String[] { "." }, StringSplitOptions.RemoveEmptyEntries).ToList();
                        foreach (String elem in dataElemPath)
                        {
                            data = data[elem];
                        }
                    }
                    if (helper.ActiveTable.child_data_element != null && helper.ActiveTable.child_data_element.ToString() != "")
                    {
                        String childDataElemParam = helper.ActiveTable.child_data_element.ToString();
                        if (((Newtonsoft.Json.Linq.JArray)(data)).Count == 0)
                        {
                            helper.IsMore = false;
                            break;
                        }
                        foreach (dynamic row in data)
                        {
                            dynamic childData = row;
                            List<String> childDataElem = childDataElemParam.Split(new String[] { "." }, StringSplitOptions.RemoveEmptyEntries).ToList();
                            foreach (String elem in childDataElem)
                            {
                                if (elem == "*")
                                {
                                    try
                                    {
                                        childData = ((Newtonsoft.Json.Linq.JProperty)(childData)).Value;
                                    }
                                    catch (Exception ex)
                                    {

                                    }
                                }
                                else
                                {
                                    childData = childData[elem];
                                }
                                
                            }
                            if (childData != null)
                            {
                                foreach (dynamic childRow in childData)
                                {
                                    yield return InsertRow(childRow, qTable, row);
                                }
                            }
                            
                            if (recordsLoaded >= helper.pageInfo.LoadLimit)
                            {                           
                                helper.IsMore = false;
                                break;
                            }
                            recordsLoaded++;
                        }
                    }
                    else
                    {
                        //helper.pageInfo.CurrentPageSize = Convert.ToInt32(data.Count);
                        //helper.pageInfo.CurrentPage++;
                        if (data.GetType().Name == "JArray")
                        {
                            helper.pageInfo.CurrentPageSize = ((Newtonsoft.Json.Linq.JArray)(data)).Count;
                            if (((Newtonsoft.Json.Linq.JArray)(data)).Count == 0)
                            {
                                helper.IsMore = false;
                                break;
                            }
                            foreach (dynamic row in data)
                            {
                                if (recordsLoaded < helper.pageInfo.LoadLimit)
                                {
                                    yield return InsertRow(row, qTable, null);
                                    recordsLoaded++;
                                }
                                else
                                {
                                    helper.IsMore = false;
                                    break;
                                }
                            }
                        }
                        else
                        {
                            helper.pageInfo.CurrentPageSize = Convert.ToInt32(data.Count);
                            if (recordsLoaded < helper.pageInfo.LoadLimit)
                            {
                                yield return InsertRow(data, qTable, null);
                                recordsLoaded++;
                            }
                            else
                            {
                                helper.IsMore = false;
                                break;
                            }
                        }
                    }
                    
                }

                
                helper.pageInfo.CurrentRecord = recordsLoaded;
                if (isFromCache)
                {
                    helper.pageInfo.CurrentPage++;
                }
                else
                {
                    helper.Page();
                }
                
            }
        }

        private QvxDataRow InsertRow(dynamic sourceRow, QvxTable tableDef, dynamic parentData)
        {
            QvxDataRow destRow = new QvxDataRow();
            foreach (QvxField fieldDef in tableDef.Fields)
            {
                dynamic originalDef = helper.ActiveFields[fieldDef.FieldName];
                dynamic sourceField;
                if(originalDef.path.ToString().IndexOf("{parent}")==-1){
                    sourceField = GetSourceValue(sourceRow, originalDef.path.ToString(), originalDef.type.ToString());
                }
                else{
                    String parentPath = originalDef.path.ToString();
                    parentPath = parentPath.Replace("{parent}.", "");
                    sourceField = GetSourceValue(parentData, parentPath, originalDef.type.ToString());
                }
                
                if (sourceField != null)
                {
                    destRow[fieldDef] = sourceField.ToString();
                }
                else if (originalDef.path.ToString()=="")
                {
                    sourceField = GetSourceValue(sourceRow, null, originalDef.type.ToString());
                }
            }
            //recordsLoaded++;
            return destRow;
        }

        private dynamic GetSourceValue(dynamic row, String path, String type)
        {
            dynamic result = row;
            if (!String.IsNullOrEmpty(path))
            {
                string[] Children = path.Split(new string[] { "." }, StringSplitOptions.RemoveEmptyEntries);
                foreach (String s in Children)
                {
                    if (result.GetType().Name != "JArray")
                    {
                        if (result[s] != null)
                        {
                            result = result[s];
                        }
                        else
                        {
                            return null;
                        }
                    }
                    else
                    {
                        return null;
                    }

                }
            }
            
            return convertToType(result, type);
            
        }

        private dynamic convertToType(dynamic value, String type)
        {
            switch (type)
            {
                case "String":
                    return value.ToString();
                case "Boolean":
                    return Boolean.Parse(value.ToString());
                case "Integer":
                    return Int32.Parse(value.ToString());
                default:
                    return value.ToString();
            }
        }

        public override QvxDataTable ExtractQuery(string query, List<QvxTable> qvxTables)
        {
            QvxLog.Log(QvxLogFacility.Application, QvxLogSeverity.Notice, "Extracting Query");
            //Debugger.Launch();
            //NOTE: Where clause not yet supported
            String fields = "";
            String where = "";
            Int16 limit;
            query = query.Replace("\r\n", " ").Replace("\n", " ");
            try
            {
                Match match;
                match = Regex.Match(query, @"(?:select\s(?<fields>[^\/\r\n]*))\s(?:from\s(?<table>[^\/\r\n\s]+))\s*(?:where\s(?<where>[^\r\n\s]*))?(?:\s*)(?:limit\s(?<limit>[^\/\r\n\s]*))?(?:\s*)(?<cache>cache)?", RegexOptions.IgnoreCase);
                
                if (!match.Success)
                {
                    QvxLog.Log(QvxLogFacility.Application, QvxLogSeverity.Error, string.Format("ExtractQueryAndTransmitTableHeader() - QvxPleaseSendReplyException({0}, \"Invalid query: {1}\")", QvxResult.QVX_SYNTAX_ERROR, query));
                }
                //Establish Table Name
                fields = match.Groups["fields"].Value;
                fields = fields.Trim();
                if(match.Groups["where"]!=null){
                    where = match.Groups["where"].Value;
                    where = where.Trim();
                }
                if (match.Groups["limit"] != null)
                {
                    try
                    {
                        limit = Int16.Parse(match.Groups["limit"].Value);
                        helper.pageInfo.LoadLimit = limit;
                    }
                    catch (Exception ex)
                    {

                    }
                }
                liveTable = match.Groups["table"].Value;
                if (match.Groups["cache"].Value.ToLower() == "cache")
                {
                    helper.addTableToCacheList(liveTable);
                }
            }
            catch (Exception ex)
            {
                QvxLog.Log(QvxLogFacility.Application, QvxLogSeverity.Error, ex.Message);
            }
            if (!String.IsNullOrEmpty(liveTable) && helper!=null)
            {
                helper.SetActiveTable(liveTable, where);
                //Create QvxTable based on fields in Select statement
                MTables = new List<QvxTable>();
                QvxTable qT = new QvxTable { TableName = liveTable, Fields = helper.createFieldList(liveTable, fields), GetRows = GetData };
                MTables.Add(qT);
                return new QvxDataTable(qT);
            }
            return null;

            //return table
        }
    }
}
