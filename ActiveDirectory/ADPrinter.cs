using System;
using System.Collections.Generic;
using System.DirectoryServices;

namespace ActiveDirectory
{
    /// <summary>
    /// Represents an Active Directory printer (printQueue) object.
    /// Provides typed accessors for printer hardware characteristics, driver information,
    /// and printing capabilities published in the directory.
    /// </summary>
    public class ADPrinter : ADObject
    {
        /// <summary>Parameterless constructor for use when <see cref="ADObject.adobject"/> is set separately.</summary>
        public ADPrinter() { }

        /// <summary>Constructs an <see cref="ADPrinter"/> from its AD GUID.</summary>
        /// <param name="guid">The printer object's AD GUID.</param>
        public ADPrinter(ADGuid guid) : base(guid) { }

        /// <summary>Constructs an <see cref="ADPrinter"/> wrapping an existing <see cref="DirectoryEntry"/>.</summary>
        /// <param name="entry">An already-opened <see cref="DirectoryEntry"/> for a printer object.</param>
        public ADPrinter(DirectoryEntry entry) : base(entry) { }

        /// <summary>Constructs an <see cref="ADPrinter"/> from a full LDAP path or distinguished name.</summary>
        /// <param name="ldap">A full LDAP path (e.g. <c>LDAP://CN=...,DC=...</c>) or a bare DN.</param>
        public ADPrinter(String ldap) : base(ldap) { }

        /// <summary>Gets the printer's published name as shown to users (the <c>printerName</c> attribute).</summary>
        public String PrinterName
        {
            get { return GetProperty(ADProperties.Printer.PrinterName); }
        }

        /// <summary>Gets the printer object's description (the <c>description</c> attribute).</summary>
        public String Description
        {
            get { return GetProperty(ADProperties.Printer.Description); }
        }

        /// <summary>Gets the physical location of the printer as published in the directory (the <c>location</c> attribute).</summary>
        public String Location
        {
            get { return GetProperty(ADProperties.Printer.Location); }
        }

        /// <summary>Gets the fully-qualified name of the print server hosting this printer (the <c>serverName</c> attribute).</summary>
        public String ServerName
        {
            get { return GetProperty(ADProperties.Printer.ServerName); }
        }

        /// <summary>Gets the NetBIOS (short) name of the print server (the <c>shortServerName</c> attribute).</summary>
        public String ServerShortName
        {
            get { return GetProperty(ADProperties.Printer.ServerShortName); }
        }

        /// <summary>Gets the port name used by the print server to communicate with the printer (the <c>portName</c> attribute).</summary>
        public String PortName
        {
            get { return GetProperty(ADProperties.Printer.PortName); }
        }

        /// <summary>Gets the name of the printer driver installed on the print server (the <c>driverName</c> attribute).</summary>
        public String DriverName
        {
            get { return GetProperty(ADProperties.Printer.DriverName); }
        }

        /// <summary>Gets the version number of the printer driver (the <c>driverVersion</c> attribute), or <c>null</c> if not set.</summary>
        public Nullable<Int32> DriverVersion
        {
            get { return base.GetInt32(ADProperties.Printer.DriverVersion); }
        }

        /// <summary>Gets the list of paper-bin names available on this printer (the <c>printBinNames</c> attribute).</summary>
        public List<String> BinNames
        {
            get { return base.GetList(ADProperties.Printer.BinNames); }
        }

        /// <summary>Gets the maximum print resolution supported by this printer in DPI (the <c>printMaxResolutionSupported</c> attribute), or <c>null</c> if not set.</summary>
        public Nullable<Int32> MaxResolutionSupported
        {
            get { return base.GetInt32(ADProperties.Printer.MaxResolutionSupported); }
        }

        /// <summary>Gets the list of page orientations this printer supports (the <c>printOrientationsSupported</c> attribute, e.g. <c>"PORTRAIT"</c>, <c>"LANDSCAPE"</c>).</summary>
        public List<String> OrientationsSupported
        {
            get { return base.GetList(ADProperties.Printer.OrientationsSupported); }
        }

        /// <summary>Gets whether this printer supports output collation (the <c>printCollate</c> attribute).</summary>
        public bool Collate
        {
            get { return GetBool(ADProperties.Printer.Collate); }
        }

        /// <summary>Gets whether this printer supports colour printing (the <c>printColor</c> attribute).</summary>
        public bool Color
        {
            get { return GetBool(ADProperties.Printer.Color); }
        }

        /// <summary>Gets the page-description language used by this printer, such as PCL or PostScript (the <c>printLanguage</c> attribute).</summary>
        public String Language
        {
            get { return GetProperty(ADProperties.Printer.Language); }
        }

        /// <summary>Gets the network share name used to connect to this printer (the <c>printShareName</c> attribute).</summary>
        public String ShareName
        {
            get { return GetProperty(ADProperties.Printer.ShareName); }
        }

        /// <summary>Gets the spooling mode for this printer, indicating whether jobs are spooled or printed directly (the <c>printSpooling</c> attribute).</summary>
        public String Spooling
        {
            get { return GetProperty(ADProperties.Printer.Spooling); }
        }

        /// <summary>Gets whether completed print jobs are retained in the spooler after printing (the <c>printKeepPrintedJobs</c> attribute).</summary>
        public bool KeepPrintedJobs
        {
            get { return GetBool(ADProperties.Printer.KeepPrintedJobs); }
        }

        /// <summary>Gets whether this printer has a built-in stapler (the <c>printStaplingSupported</c> attribute).</summary>
        public bool StaplingSupported
        {
            get { return GetBool(ADProperties.Printer.StaplingSupported); }
        }

        /// <summary>Gets the amount of printer memory in kilobytes (the <c>printMemory</c> attribute), or <c>null</c> if not set.</summary>
        public Nullable<Int32> Memory
        {
            get { return base.GetInt32(ADProperties.Printer.Memory); }
        }

        /// <summary>Gets the list of media types currently loaded in the printer (the <c>printMediaReady</c> attribute).</summary>
        public List<String> MediaReady
        {
            get { return base.GetList(ADProperties.Printer.MediaReady); }
        }

        /// <summary>Gets the list of all media types this printer is capable of handling (the <c>printMediaSupported</c> attribute).</summary>
        public List<String> MediaSupported
        {
            get { return base.GetList(ADProperties.Printer.MediaSupported); }
        }

        /// <summary>Gets the rated print speed for this printer in pages per minute (the <c>printPagesPerMinute</c> attribute), or <c>null</c> if not set.</summary>
        public Nullable<Int32> PagesPerMinute
        {
            get { return base.GetInt32(ADProperties.Printer.PagesPerMinute); }
        }

        /// <summary>Gets whether this printer supports duplex (double-sided) printing (the <c>printDuplexSupported</c> attribute).</summary>
        public bool DuplexSupported
        {
            get { return GetBool(ADProperties.Printer.DuplexSupported); }
        }
    }
}
