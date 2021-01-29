﻿using System;
using System.Collections.Generic;
using System.Linq;
using Altairis.Fakturoid.Client;
using InvoicingImport.Data;

namespace InvoicingImport {
    internal class Program {
        private static FakturoidContext context;
        private static Dictionary<int, int> subjectIdMap;

        private static void Main(string[] args) {
            // Verify commandline arguments 
            if (args.Length != 3) {
                Console.WriteLine("USAGE: InvoicingImport accountname email token");
                return;
            }
            var accountName = args[0];
            var email = args[1];
            var accountToken = args[2];

            // Create API context
            context = new FakturoidContext(accountName, email, accountToken);

            // Process all operations
            PurgeAll();
            ImportContacts();
            ImportInvoices();
        }
        
        private static void ImportInvoices() {
            Console.WriteLine("Importing invoices:");
            using (var dc = new InvoicingEntities()) {
                foreach (var i in dc.Invoices) {
                    // Compute invoice number
                    var invoiceNumber = string.Format("{0}01{1:00000}", i.DateTaxed.Year - 1980, i.SeqId);

                    // Map old contact ID to new
                    var subjectId = subjectIdMap[i.ContactId];
                    Console.Write("  #{0}: {1}...", i.InvoiceId, invoiceNumber);

                    // Create new invoice entity
                    var newInvoice = new JsonInvoice {
                        number = invoiceNumber,
                        subject_id = subjectId,
                        issued_on = i.DateCreated,
                        taxable_fulfillment_due = i.DateTaxed,
                        due_on = i.DateDue,
                        lines = new List<JsonInvoiceLine>()
                    };

                    // Add invoice items
                    foreach (var l in i.Items) {
                        newInvoice.lines.Add(new JsonInvoiceLine {
                            name = l.Name,
                            quantity = l.Quantity,
                            unit_name = l.Unit,
                            unit_price = l.UnitPrice,
                            vat_rate = l.Tax
                        });
                    }
                    
                    // Create invoice
                    var newInvoiceId = context.Invoices.Create(newInvoice);
                    Console.WriteLine("OK, ID={0}", newInvoiceId);

                    // Get payment status
                    if (i.Payments.Any()) {
                        Console.Write("  #{0}: Updating payment status...", i.InvoiceId);
                        var datePaid = i.Payments.Max(x => x.DateReceived);
                        context.Invoices.SetPaymentStatus(newInvoiceId, InvoicePaymentStatus.Paid, datePaid);
                    }
                    else {
                        Console.Write("  #{0}: Updating delivery status...", i.InvoiceId);
                        context.Invoices.SendMessage(newInvoiceId, InvoiceMessageType.NoMessage);
                    }
                    Console.WriteLine("OK");
                }
            }
        }

        private static void ImportContacts() {
            Console.WriteLine("Importing contacts:");
            subjectIdMap = new Dictionary<int, int>();
            using (var dc = new InvoicingEntities()) {
                foreach (var c in dc.Contacts) {
                    try {
                        Console.Write("  #{0}: {1}...", c.ContactId, c.CompanyName);
                        var newSubject = new JsonSubject {
                            bank_account = c.BankAccountNumber,
                            city = c.DefaultBillAddress.City,
                            country = c.DefaultBillAddress.Country,
                            custom_id = c.ContactId.ToString(),
                            email = c.ContactEmail,
                            full_name = c.ContactName,
                            name = c.CompanyName,
                            phone = c.ContactMobile,
                            registration_no = c.ICO,
                            street = c.DefaultBillAddress.Street,
                            vat_no = c.DIC,
                            zip = c.DefaultBillAddress.ZIP
                        };
                        var newId = context.Subjects.Create(newSubject);
                        Console.WriteLine("OK, ID={0}", newId);
                        subjectIdMap.Add(c.ContactId, newId);
                    }
                    catch (FakturoidException) {
                        Console.WriteLine("Failed!");
                    }
                }
            }


        }

        private static void PurgeAll() {
            // Delete all invoices
            Console.Write("Listing all invoices...");
            var invoices = context.Invoices.Select();
            Console.WriteLine("OK");

            Console.WriteLine("Deleting invoices:");
            foreach (var invoice in invoices) {
                Console.Write("  #{0}: ({1}) {2}, {3:N2} Kč...", invoice.id, invoice.number, invoice.client_name, invoice.native_total);
                try {
                    context.Invoices.Delete(invoice.id);
                    Console.WriteLine("OK");
                }
                catch (FakturoidException fex) {
                    Console.WriteLine(fex.Message);
                }
            }

            // Delete all subjects
            Console.Write("Listing all subjects...");
            var subjects = context.Subjects.Select();
            Console.WriteLine("OK");

            Console.WriteLine("Deleting subjects:");
            foreach (var subject in subjects) {
                Console.Write("  #{0}: {1}...", subject.id, subject.name);
                try {
                    context.Subjects.Delete(subject.id);
                    Console.WriteLine("OK");
                }
                catch (FakturoidException fex) {
                    Console.WriteLine(fex.Message);
                }
            }
        }

    }
}
