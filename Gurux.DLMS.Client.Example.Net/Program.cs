//
// --------------------------------------------------------------------------
//  Gurux Ltd
//
//
//
// Filename:        $HeadURL$
//
// Version:         $Revision$,
//                  $Date$
//                  $Author$
//
// Copyright (c) Gurux Ltd
//
//---------------------------------------------------------------------------
//
//  DESCRIPTION
//
// This file is a part of Gurux Device Framework.
//
// Gurux Device Framework is Open Source software; you can redistribute it
// and/or modify it under the terms of the GNU General Public License
// as published by the Free Software Foundation; version 2 of the License.
// Gurux Device Framework is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
// See the GNU General Public License for more details.
//
// More information of Gurux products: http://www.gurux.org
//
// This code is licensed under the GNU General Public License v2.
// Full text may be retrieved at http://www.gnu.org/licenses/gpl-2.0.txt
//---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Gurux.Serial;
using Gurux.Net;
using Gurux.DLMS.Enums;
using System.Threading;
using Gurux.DLMS.Objects;
using Gurux.MQTT;

namespace Gurux.DLMS.Client.Example
{
    class Program
    {
        static int Main(string[] args)
        {
            Settings settings = new Settings();
            Reader.GXDLMSReader reader = null;
            try
            {
                ////////////////////////////////////////
                //Handle command line parameters.
                int ret = Settings.GetParameters(args, settings);
                if (ret != 0)
                {
                    return ret;
                }
                ////////////////////////////////////////
                //Initialize connection settings.
                if (settings.media is GXSerial)
                {
                }
                else if (settings.media is GXNet)
                {
                }
                else if (settings.media is GXMqtt)
                {
                }
                else
                {
                    throw new Exception("Unknown media type.");
                }
                ////////////////////////////////////////
                reader = new Reader.GXDLMSReader(settings.client, settings.media, settings.trace, settings.invocationCounter);
                reader.OnNotification += (data) =>
                {
                    Console.WriteLine(data);
                };
                //Create manufacturer spesific custom COSEM object.
                settings.client.OnCustomObject += (type, version) =>
                {
                    /*
                    if (type == 6001 && version == 0)
                    {
                        return new ManufacturerSpesificObject();
                    }
                    */
                    return null;
                };

                try
                {
                    settings.media.Open();
                }
                catch (System.IO.IOException ex)
                {
                    Console.WriteLine("----------------------------------------------------------");
                    Console.WriteLine(ex.Message);
                    Console.WriteLine("Available ports:");
                    Console.WriteLine(string.Join(" ", GXSerial.GetPortNames()));
                    return 1;
                }
                //Some meters need a break here.
                Thread.Sleep(1000);
                Console.WriteLine("Connected:");

                if (settings.media is GXNet net && settings.client.InterfaceType == InterfaceType.CoAP)
                {
                    //Update token ID.
                    settings.client.Coap.Token = 0x45;
                    settings.client.Coap.Host = net.HostName;
                    settings.client.Coap.MessageId = 1;
                    settings.client.Coap.Port = (UInt16) net.Port;
                    //DLMS version.
                    settings.client.Coap.Options[65001] = (byte)1;
                    //Client SAP.
                    settings.client.Coap.Options[65003] = (byte)settings.client.ClientAddress;
                    //Server SAP
                    settings.client.Coap.Options[65005] = (byte)settings.client.ServerAddress;
                }
                //Export client and server certificates from the meter.
                if (!string.IsNullOrEmpty(settings.ExportSecuritySetupLN))
                {
                    reader.ExportMeterCertificates(settings.ExportSecuritySetupLN);
                }
                //Generate new client and server certificates and import them to the server.
                else if (!string.IsNullOrEmpty(settings.GenerateSecuritySetupLN))
                {
                    reader.GenerateCertificates(settings.GenerateSecuritySetupLN);
                }
                else if (settings.readObjects.Count != 0)
                {
                    bool read = false;
                    if (settings.outputFile != null)
                    {
                        try
                        {
                            settings.client.Objects.Clear();
                            settings.client.Objects.AddRange(GXDLMSObjectCollection.Load(settings.outputFile));
                            read = true;
                        }
                        catch (Exception)
                        {
                            //It's OK if this fails.
                        }
                    }
                    reader.InitializeConnection();
                    if (!read)
                    {
                        var unknownCLassId = settings.readObjects.Any(x => x.Key.Contains("-") == false);
                        if (unknownCLassId)
                        {
                            Console.Write($"Reading association: ");
                            reader.GetAssociationView(settings.outputFile);
                            Console.WriteLine($"Success");
                        }
                    }
                    foreach (KeyValuePair<string, int> it in settings.readObjects)
                    {
                        var obis = it.Key;
                        var classId = 0;
                        var version = 1;
                        if (obis.Contains("-")) //known class id 3-1.0.1.8.0.255;2
                        {
                            classId = int.Parse(it.Key.Split('-').FirstOrDefault() ?? string.Empty);
                            obis = it.Key.Split('-').LastOrDefault();
                        }
                        else //unknown class id - find it in association table
                        {
                            var reg = settings.client.Objects.FindByLN(ObjectType.None, it.Key);
                            if (reg == null)
                                continue;
                            classId = (int)reg.ObjectType;
                            version = reg.Version;
                        }

                        Console.Write($@"{(ObjectType)classId} {classId}-{obis}:{it.Value} = ");
                        var obj = GXDLMSClient.CreateObject((ObjectType)classId, (byte)version);
                        obj.LogicalName = obis;
                        object val = reader.Read(obj, it.Value);
                        reader.ShowValue(val, it.Value);
                    }
                    if (settings.outputFile != null)
                    {
                        try
                        {
                            settings.client.Objects.Save(settings.outputFile, new GXXmlWriterSettings() { UseMeterTime = true, IgnoreDefaultValues = false });
                        }
                        catch (Exception)
                        {
                            //It's OK if this fails.
                        }
                    }
                }
                else
                {
                    reader.ReadAll(settings.outputFile);
                }
            }
            catch (GXDLMSException ex)
            {
                Console.WriteLine(ex.Message);
                if (System.Diagnostics.Debugger.IsAttached)
                {
                    Console.ReadKey();
                }
                return 1;
            }
            catch (GXDLMSExceptionResponse ex)
            {
                Console.WriteLine(ex.Message);
                if (System.Diagnostics.Debugger.IsAttached)
                {
                    Console.ReadKey();
                }
                return 1;
            }
            catch (GXDLMSConfirmedServiceError ex)
            {
                Console.WriteLine(ex.Message);
                if (System.Diagnostics.Debugger.IsAttached)
                {
                    Console.ReadKey();
                }
                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.ToString());
                if (System.Diagnostics.Debugger.IsAttached)
                {
                    Console.ReadKey();
                }
                return 1;
            }
            finally
            {
                if (reader != null)
                {
                    reader.Close();
                }
                if (System.Diagnostics.Debugger.IsAttached)
                {
                    Console.WriteLine("Ended. Press any key to continue.");
                    Console.ReadKey();
                }
            }
            return 0;
        }
    }
}
