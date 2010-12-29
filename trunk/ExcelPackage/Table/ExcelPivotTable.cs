﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.IO.Packaging;
using System.Text.RegularExpressions;
using OfficeOpenXml.Table;

namespace OfficeOpenXml.Table
{
    public class ExcelPivotTable : XmlHelper
    {
        internal ExcelPivotTable(PackageRelationship rel, ExcelWorksheet sheet) : 
            base(sheet.NameSpaceManager)
        {
            WorkSheet = sheet;
            PivotTableUri = PackUriHelper.ResolvePartUri(rel.SourceUri, rel.TargetUri);
            Relationship = rel;
            var pck = sheet.xlPackage.Package;
            Part=pck.GetPart(PivotTableUri);

            PivotXml = new XmlDocument();
            PivotXml.Load(Part.GetStream());
            init();

            Address = new ExcelAddressBase(GetXmlNodeString("@ref"));
            }
        /// <summary>
        /// Add a new pivottable
        /// </summary>
        /// <param name="sheet">the Worksheet</param>
        /// <param name="address">the address of the pivottable</param>
        /// <param name="sourceAddress">The address of the Source data</param>
        /// <param name="name"></param>
        /// <param name="tblId"></param>
        internal ExcelPivotTable(ExcelWorksheet sheet, ExcelAddressBase address,ExcelAddressBase sourceAddress, string name, int tblId) : 
            base(sheet.NameSpaceManager)
	    {
            WorkSheet = sheet;
            Address = address;
            var pck = sheet.xlPackage.Package;

            PivotXml = new XmlDocument();
            PivotXml.LoadXml(GetStartXml(name, tblId, address, sourceAddress)); 
            TopNode = PivotXml.DocumentElement;
            PivotTableUri =  new Uri(string.Format("/xl/pivotTables/pivotTable{0}.xml", tblId), UriKind.Relative);
            init();

            Part = pck.CreatePart(PivotTableUri, ExcelPackage.schemaPivotTable);
            PivotXml.Save(Part.GetStream());
            
            //Worksheet-Pivottable relationship
            Relationship = sheet.Part.CreateRelationship(PackUriHelper.ResolvePartUri(sheet.WorksheetUri, PivotTableUri), TargetMode.Internal, ExcelPackage.schemaRelationships + "/pivotTable");

            _cacheDefinition = new ExcelPivotCacheDefinition(sheet.NameSpaceManager, this, sourceAddress, tblId);
            _cacheDefinition.Relationship=Part.CreateRelationship(PackUriHelper.ResolvePartUri(PivotTableUri, _cacheDefinition.CacheDefinitionUri), TargetMode.Internal, ExcelPackage.schemaRelationships + "/pivotCacheDefinition");

            sheet.Workbook.AddPivotTable(CacheID.ToString(), _cacheDefinition.CacheDefinitionUri);

            LoadFields();

            using (var r=sheet.Cells[address.Address])
            {
                r.Clear();
            }
        }
        private void init()
        {
            SchemaNodeOrder = new string[] { "location", "pivotFields", "rowFields", "rowItems", "colFields","colItems","pageFields","pageItems","dataFields","dataItems","pivotTableStyleInfo" };
        }
        private void LoadFields()
        {
            Fields.Clear();
            int ix=0;
            foreach(XmlElement fieldNode in PivotXml.SelectNodes("//d:pivotFields/d:pivotField",NameSpaceManager))
            {
                Fields.AddInternal(new ExcelPivotTableField(NameSpaceManager, fieldNode, this, ix++));
            }
        }
        private string GetStartXml(string name, int id, ExcelAddressBase address, ExcelAddressBase sourceAddress)
        {
            string xml = string.Format("<pivotTableDefinition xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" name=\"{0}\" cacheId=\"{1}\" dataOnRows=\"1\" applyNumberFormats=\"0\" applyBorderFormats=\"0\" applyFontFormats=\"0\" applyPatternFormats=\"0\" applyAlignmentFormats=\"0\" applyWidthHeightFormats=\"1\" dataCaption=\"Data\" updatedVersion=\"3\" showMemberPropertyTips=\"0\" useAutoFormatting=\"1\" itemPrintTitles=\"1\" createdVersion=\"1\" indent=\"0\" compact=\"0\" compactData=\"0\" gridDropZones=\"1\">",name, id);

            xml+=string.Format("<location ref=\"{0}\" firstHeaderRow=\"1\" firstDataRow=\"1\" firstDataCol=\"1\" /> ", address.FirstAddress);
            xml+=string.Format("<pivotFields count=\"{0}\">", sourceAddress._toCol-sourceAddress._fromCol+1);
            for (int col = sourceAddress._fromCol; col <= sourceAddress._toCol; col++)
            {
                xml += "<pivotField showAll=\"0\" />"; //compact=\"0\" outline=\"0\" subtotalTop=\"0\" includeNewItemsInFilter=\"1\" 
                
            }
            xml+="</pivotFields>";
            xml+="<pivotTableStyleInfo name=\"PivotStyleMedium9\" showRowHeaders=\"1\" showColHeaders=\"1\" showRowStripes=\"0\" showColStripes=\"0\" showLastColumn=\"1\" />";
            xml+="</pivotTableDefinition>";
            return xml;
        }
        internal PackagePart Part
        {
            get;
            set;
        }
        public XmlDocument PivotXml { get; private set; }
        public Uri PivotTableUri
        {
            get;
            internal set;
        }
        internal PackageRelationship Relationship
        {
            get;
            set;
        }
        const string ID_PATH = "@id";
        internal int Id
        {
            get
            {
                return GetXmlNodeInt(ID_PATH);
            }
            set
            {
                SetXmlNodeString(ID_PATH, value.ToString());
            }
        }
        const string NAME_PATH = "@name";
        const string DISPLAY_NAME_PATH = "@displayName";
        /// <summary>
        /// Name of the table object in Excel
        /// </summary>
        public string Name
        {
            get
            {
                return GetXmlNodeString(NAME_PATH);
            }
            set
            {
                if (WorkSheet.Workbook.ExistsTableName(value))
                {
                    throw (new ArgumentException("Tablename is not unique"));
                }
                string prevName = Name;
                if (WorkSheet.Tables._tableNames.ContainsKey(prevName))
                {
                    int ix = WorkSheet.Tables._tableNames[prevName];
                    WorkSheet.Tables._tableNames.Remove(prevName);
                    WorkSheet.Tables._tableNames.Add(value, ix);
                }
                SetXmlNodeString(NAME_PATH, value);
                SetXmlNodeString(DISPLAY_NAME_PATH, cleanDisplayName(value));
            }
        }        
        ExcelPivotCacheDefinition _cacheDefinition = null;
        public ExcelPivotCacheDefinition CacheDefinition
        {
            get
            {
                if (_cacheDefinition == null)
                {
                    _cacheDefinition = new ExcelPivotCacheDefinition(NameSpaceManager, null,null, 1);
                }
                return _cacheDefinition;
            }
        }
        private string cleanDisplayName(string name)
        {
            return Regex.Replace(name, @"[^\w\.-_]", "_");
        }
        #region "Public Properties"

        /// <summary>
        /// The worksheet of the table
        /// </summary>
        public ExcelWorksheet WorkSheet
        {
            get;
            set;
        }
        /// <summary>
        /// The address of the table
        /// </summary>
        public ExcelAddressBase Address
        {
            get;
            internal set;
        }
        public bool DataOnRows 
        { 
            get
            {
                return GetXmlNodeBool("@dataOnRows");
            }
            set
            {
                SetXmlNodeBool("@dataOnRows",value);
            }
        }
        public bool ApplyNumberFormats 
        { 
            get
            {
                return GetXmlNodeBool("@applyNumberFormats");
            }
            set
            {
                SetXmlNodeBool("@applyNumberFormats",value);
            }
        }
        public bool ApplyBorderFormats 
        { 
            get
            {
                return GetXmlNodeBool("@applyBorderFormats");
            }
            set
            {
                SetXmlNodeBool("@applyBorderFormats",value);
            }
        }
        public bool ApplyFontFormats
        { 
            get
            {
                return GetXmlNodeBool("@applyFontFormats");
            }
            set
            {
                SetXmlNodeBool("@applyFontFormats",value);
            }
        }
        public bool ApplyPatternFormats
        { 
            get
            {
                return GetXmlNodeBool("@applyPatternFormats");
            }
            set
            {
                SetXmlNodeBool("@applyPatternFormats",value);
            }
        }
        public bool ApplyWidthHeightFormats
        { 
            get
            {
                return GetXmlNodeBool("@applyWidthHeightFormats");
            }
            set
            {
                SetXmlNodeBool("@applyWidthHeightFormats",value);
            }
        }
        public bool ShowMemberPropertyTips
        { 
            get
            {
                return GetXmlNodeBool("@showMemberPropertyTips");
            }
            set
            {
                SetXmlNodeBool("@showMemberPropertyTips",value);
            }
        } 
        public bool UseAutoFormatting
        { 
            get
            {
                return GetXmlNodeBool("@useAutoFormatting");
            }
            set
            {
                SetXmlNodeBool("@useAutoFormatting",value);
            }
        } 
        public bool ItemPrintTitles
        { 
            get
            {
                return GetXmlNodeBool("@itemPrintTitles");
            }
            set
            {
                SetXmlNodeBool("@itemPrintTitles",value);
            }
        }
        public bool GridDropZones
        { 
            get
            {
                return GetXmlNodeBool("@gridDropZones");
            }
            set
            {
                SetXmlNodeBool("@gridDropZones",value);
            }
        }
        public int Indent
        { 
            get
            {
                return GetXmlNodeInt("@indent");
            }
            set
            {
                SetXmlNodeString("@indent",value.ToString());
            }
        }        
        public bool Compact
        { 
            get
            {
                return GetXmlNodeBool("@compact");
            }
            set
            {
                SetXmlNodeBool("@compact",value);
            }
        }        
        public bool CompactData
        { 
            get
            {
                return GetXmlNodeBool("@compactData");
            }
            set
            {
                SetXmlNodeBool("@compactData",value);
            }
        }
        public string GrandTotalCaption
        {
            get
            {
                return GetXmlNodeString("@grandTotalCaption");
            }
            set
            {
                SetXmlNodeString("@grandTotalCaption", value);
            }
        }
        const string FIRSTHEADERROW_PATH="d:location/@firstHeaderRow";
        public int FirstHeaderRow
        {
            get
            {
                return GetXmlNodeInt(FIRSTHEADERROW_PATH);
            }
            set
            {
                SetXmlNodeString(FIRSTHEADERROW_PATH, value.ToString());
            }
        }
        const string FIRSTDATAROW_PATH = "d:location/@firstDataRow";
        public int FirstDataRow
        {
            get
            {
                return GetXmlNodeInt(FIRSTDATAROW_PATH);
            }
            set
            {
                SetXmlNodeString(FIRSTDATAROW_PATH, value.ToString());
            }
        }
        const string FIRSTDATACOL_PATH = "d:location/@firstDataCol";
        public int FirstDataCol
        {
            get
            {
                return GetXmlNodeInt(FIRSTDATACOL_PATH);
            }
            set
            {
                SetXmlNodeString(FIRSTDATACOL_PATH, value.ToString());
            }
        }
        ExcelPivotTableFieldCollectionBase _fields = null;
        public ExcelPivotTableFieldCollectionBase Fields
        {
            get
            {
                if (_fields == null)
                {
                    _fields = new ExcelPivotTableFieldCollectionBase(this);
                }
                return _fields;
            }
        }
        ExcelPivotTableFieldCollection _rowFields = null;
        public ExcelPivotTableFieldCollection RowFields
        {
            get
            {
                if (_rowFields == null)
                {
                    _rowFields = new ExcelPivotTableFieldCollection(this, "rowFields");
                }
                return _rowFields;
            }
        }
        ExcelPivotTableFieldCollection _columnFields = null;
        public ExcelPivotTableFieldCollection ColumnFields
        {
            get
            {
                if (_columnFields == null)
                {
                    _columnFields = new ExcelPivotTableFieldCollection(this, "colFields");
                }
                return _columnFields;
            }
        }
        ExcelPivotTableFieldCollection _dataFields = null;
        public ExcelPivotTableFieldCollection DataFields
        {
            get
            {
                if (_dataFields == null)
                {
                    _dataFields = new ExcelPivotTableFieldCollection(this, "dataFields");
                }
                return _dataFields;
            }
        }
        ExcelPivotTableFieldCollection _pageFields = null;
        public ExcelPivotTableFieldCollection PageFields
        {
            get
            {
                if (_pageFields == null)
                {
                    _pageFields = new ExcelPivotTableFieldCollection(this, "pageFields");
                }
                return _pageFields;
            }
        }
        const string STYLENAME_PATH = "d:pivotTableStyleInfo/@name";
        public string StyleName
        {
            get
            {
                return GetXmlNodeString(StyleName);
            }
            set
            {
                if (value.StartsWith("PivotStyle"))
                {
                    try
                    {
                        _tableStyle = (TableStyles)Enum.Parse(typeof(TableStyles), value.Substring(10, value.Length - 10), true);
                    }
                    catch
                    {
                        _tableStyle = TableStyles.Custom;
                    }
                }
                else if (value == "None")
                {
                    _tableStyle = TableStyles.None;
                    value = "";
                }
                else
                {
                    _tableStyle = TableStyles.Custom;
                }
                SetXmlNodeString(STYLENAME_PATH, value, true);
            }
        }
        TableStyles _tableStyle = Table.TableStyles.Medium6;
        /// <summary>
        /// The table style. If this property is cusom the style from the StyleName propery is used.
        /// </summary>
        public TableStyles TableStyle
        {
            get
            {
                return _tableStyle;
            }
            set
            {
                _tableStyle=value;
                if (value != TableStyles.Custom)
                {
                    SetXmlNodeString(STYLENAME_PATH, "PivotStyle" + value.ToString());
                }
            }
        }

        #endregion
        #region "Internal Properties"
        internal int CacheID 
        { 
                get
                {
                    return GetXmlNodeInt("@cacheId");
                }
                set
                {
                    SetXmlNodeString("@cacheId",value.ToString());
                }
        }

        #endregion
    }
}
