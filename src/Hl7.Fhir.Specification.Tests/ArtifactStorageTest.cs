﻿/* 
 * Copyright (c) 2014, Furore (info@furore.com) and contributors
 * See the file CONTRIBUTORS for details.
 * 
 * This file is licensed under the BSD 3-Clause license
 * available at https://raw.githubusercontent.com/ewoutkramer/fhir-net-api/master/LICENSE
 */

using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hl7.Fhir.Model;
using Hl7.Fhir.Support;
using System.Diagnostics;
using System.IO;
using Hl7.Fhir.Introspection;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Specification.Source;

namespace Hl7.Fhir.Specification.Tests
{
    [TestClass]
#if PORTABLE45
	public class PortableArtifactStorageTest
#else
    public class ArtifactStorageTest
#endif
    {
#if !PORTABLE45
        [TestMethod]
        public void ZipCacherShouldCache()
        {
            var cacheKey = Guid.NewGuid().ToString();
            var zipFile = Path.Combine(Directory.GetCurrentDirectory(),"validation-min.zip");

            var fa = new ZipCacher(zipFile,cacheKey);

            Assert.IsFalse(fa.IsActual());

            var sw = new Stopwatch();

            sw.Start();
            fa.GetContents();
            sw.Stop();

            var firstRun = sw.ElapsedMilliseconds;

            Assert.IsTrue(fa.IsActual());

            sw.Restart();
            fa.GetContents();
            sw.Stop();

            var secondRun = sw.ElapsedMilliseconds;

            Assert.IsTrue(firstRun > secondRun);

            fa = new ZipCacher(zipFile,cacheKey);

            Assert.IsTrue(fa.IsActual());

            sw.Start();
            fa.GetContents();
            sw.Stop();

            var thirdRun = sw.ElapsedMilliseconds;
            Assert.IsTrue(thirdRun < firstRun);

            fa.Clear();
            Assert.IsFalse(fa.IsActual());

            sw.Restart();
            fa.GetContents();
            sw.Stop();

            var fourthRun = sw.ElapsedMilliseconds;
            Assert.IsTrue(fourthRun > secondRun);

            File.SetLastWriteTime(zipFile, DateTime.Now);
            Assert.IsFalse(fa.IsActual());
        }
#endif

        private void copy(string dir, string file, string outputDir)
        {
            File.Copy(Path.Combine(dir, file), Path.Combine(outputDir, file));
        }

        private string prepareExampleDirectory()
        {
            var zipFile = Path.Combine(Directory.GetCurrentDirectory(), "validation-min.zip");
            var zip = new ZipCacher(zipFile);
            var zipPath = zip.GetContentDirectory();

            var testPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(testPath);

            copy(zipPath, "extension-definitions.xml", testPath);
            copy(zipPath, "flag.xsd", testPath);
            copy(zipPath, "patient.sch", testPath);
            copy(@"TestData", "TestPatient.xml", testPath);
            copy(@"TestData", "TestValueSet.xml", testPath);

            Directory.CreateDirectory(Path.Combine(testPath, "sub"));
            copy(@"TestData", "TestPatient.json", testPath);

            return testPath;
        }


        [TestMethod]
        public void UseFileArtifactSource()
        {
            var testPath = prepareExampleDirectory();
            var fa = new FileArtifactSource(testPath);
            fa.Mask = "*.xml|*.xsd";
            var names = fa.ListArtifactNames();

            Assert.AreEqual(4, names.Count());
            Assert.IsTrue(names.Contains("extension-definitions.xml"));
            Assert.IsTrue(names.Contains("flag.xsd"));
            Assert.IsFalse(names.Contains("patient.sch"));

            using (var stream = fa.ReadContentArtifact("TestPatient.xml"))
            {
                var pat = FhirParser.ParseResource(FhirParser.XmlReaderFromStream(stream));
                Assert.IsNotNull(pat);
            }

            var vs = fa.ReadConformanceResource("http://hl7.org/fhir/StructureDefinition/iso21090-ADXP-streetNameBase") as StructureDefinition;
           
            Assert.IsNotNull(vs);

            var cis = fa.ListConformanceResources();
            foreach (var ci in cis) Debug.WriteLine(ci.ToString());
        }

        [TestMethod]
        public void ReadsSubdirectories()
        {
            var testPath = prepareExampleDirectory();
            var fa = new FileArtifactSource(testPath, includeSubdirectories:true);
            var names = fa.ListArtifactNames();

            Assert.AreEqual(6,names.Count());
        }

        [TestMethod]
        public void GetSomeBundledArtifacts()
        {
            var za = new CoreZipArtifactSource();

            using (var a = za.ReadContentArtifact("patient.sch"))
            {
                Assert.IsNotNull(a);
            }

            using (var a = za.ReadContentArtifact("v3-codesystems.xml"))
            {
                Assert.IsNotNull(a);
            }

            using (var a = za.ReadContentArtifact("patient.xsd"))
            {
                Assert.IsNotNull(a);
            }
        }


        [TestMethod]
        public void GetSomeArtifactsById()
        {
            var fa = new CoreZipArtifactSource();

            var vs = fa.ReadConformanceResource("http://hl7.org/fhir/v2/vs/0292");
            Assert.IsNotNull(vs);
            Assert.IsTrue(vs is ValueSet);

            vs = fa.ReadConformanceResource("http://hl7.org/fhir/vs/location-status");
            Assert.IsNotNull(vs);
            Assert.IsTrue(vs is ValueSet);

            var rs = fa.ReadConformanceResource("http://hl7.org/fhir/StructureDefinition/Condition");
            Assert.IsNotNull(rs);
            Assert.IsTrue(rs is StructureDefinition);

            rs = fa.ReadConformanceResource("http://hl7.org/fhir/StructureDefinition/ValueSet");
            Assert.IsNotNull(rs);
            Assert.IsTrue(rs is StructureDefinition);

            var dt = fa.ReadConformanceResource("http://hl7.org/fhir/StructureDefinition/Money");
            Assert.IsNotNull(dt);
            Assert.IsTrue(dt is StructureDefinition);

            // Try to find a core extension
            var ext = fa.ReadConformanceResource("http://hl7.org/fhir/StructureDefinition/diagnosticorder-reason");
            Assert.IsNotNull(ext);
            Assert.IsTrue(ext is StructureDefinition);

            // Try to find an additional US profile (they are distributed with the spec for now)
            var us = fa.ReadConformanceResource("http://hl7.org/fhir/StructureDefinition/cond-uslab-uslabcond");
            Assert.IsNotNull(us);
            Assert.IsTrue(us is StructureDefinition);           
        }


        [TestMethod]
        public void TestSetupIsOnce()
        {
            var fa = new CoreZipArtifactSource();

            var sw = new Stopwatch();
            sw.Start();
            var vs = fa.ReadConformanceResource("http://hl7.org/fhir/v2/vs/0292");
            sw.Stop();

            var sw2 = new Stopwatch();
            sw2.Start();
            var vs2 = fa.ReadConformanceResource("http://hl7.org/fhir/v2/vs/0292");
            sw2.Stop();

            Assert.IsTrue(sw2.ElapsedMilliseconds < sw.ElapsedMilliseconds);
            Debug.WriteLine(String.Format("First time {0}, second time {1}", sw.ElapsedMilliseconds, sw2.ElapsedMilliseconds));
        }


        [TestMethod]
        public void RetrieveWebArtifact()
        {
            var wa = new WebArtifactSource();

            var artifact = wa.ReadConformanceResource("http://fhir-dev.healthintersections.com.au/open/StructureDefinition/Alert");

            Assert.IsNotNull(artifact);
            Assert.IsTrue(artifact is StructureDefinition);
            Assert.AreEqual("Alert", ((StructureDefinition)artifact).Name);
        }

        [TestMethod]
        public void RetrieveArtifactMulti()
        {
            var resolver = ArtifactResolver.CreateDefault();

            var vs = resolver.ReadConformanceResource("http://hl7.org/fhir/v2/vs/0292");
            Assert.IsNotNull(vs);
            Assert.IsTrue(vs is ValueSet);

            using (var a = resolver.ReadContentArtifact("patient.sch"))
            {

                Assert.IsNotNull(a);
            }

            var artifact = resolver.ReadConformanceResource("http://fhir-dev.healthintersections.com.au/open/StructureDefinition/flag");

            Assert.IsNotNull(artifact);
            Assert.IsTrue(artifact is StructureDefinition);
            Assert.AreEqual("Alert", ((StructureDefinition)artifact).Name);
        }

        [TestMethod]
        public void TestSourceCaching()
        {
            var src = new CachedArtifactSource(ArtifactResolver.CreateDefault());

            Stopwatch sw1 = new Stopwatch();

            // Ensure looking up a failed endpoint repeatedly does not cost much time
            sw1.Start();
            src.ReadConformanceResource("http://some.none.existant.address.nl");
            sw1.Stop();

            var sw2 = new Stopwatch();

            sw2.Start();
            src.ReadConformanceResource("http://some.none.existant.address.nl");
            sw2.Stop();

            Debug.WriteLine("sw2 {0}, sw1 {1}", sw2.ElapsedMilliseconds, sw1.ElapsedMilliseconds);
            Assert.IsTrue(sw2.ElapsedMilliseconds <= sw1.ElapsedMilliseconds && sw2.ElapsedMilliseconds < 100);

            // Now try an existing artifact
            sw1.Restart();
            src.ReadConformanceResource("http://hl7.org/fhir/v2/vs/0292");
            sw1.Stop();

            sw2.Restart();
            src.ReadConformanceResource("http://hl7.org/fhir/v2/vs/0292");
            sw2.Stop();

            Assert.IsTrue(sw2.ElapsedMilliseconds < sw1.ElapsedMilliseconds && sw2.ElapsedMilliseconds < 100);

        }
    }
}