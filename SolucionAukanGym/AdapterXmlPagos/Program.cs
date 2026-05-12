using System;
using System.IO;
using System.Linq;
using System.Messaging;
using System.Xml.Linq;

namespace AdapterXMLPagos{
    internal class Program{
        private const string CarpetaXML = @"C:\AukanGym\PagosXML" ;
        private const string ColaMSMQ = @".\private$\jdo_suc_pagos";

        static void Main(string[] args){
            Console.WriteLine("===Adapter XML pagos - Aukan GYM ===");
            try
            {
                ValidarCola();

                string fecha = DateTime.Now.ToString("yyyyMMdd");
                if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
                {
                    fecha = args[0].Trim();
                }

                Console.WriteLine("FEcha deproceso: " + fecha);
                Console.WriteLine("Carpeta XML: " + CarpetaXML);
                Console.WriteLine("Cola MSMQ destino: " + ColaMSMQ);

                string patron = $"suc_*-pagos-{fecha}.xml";
                string[] archivos = Directory.GetFiles(CarpetaXML, patron);

                if (archivos.Length == 0)
                {
                    Console.WriteLine("No se encontraron archivos con el patron");
                    Console.WriteLine("Presione una tecla para salir");
                    Console.ReadKey();
                    return;
                }

                int totalMensajes = 0;

                foreach (string archivo in archivos)
                {
                    Console.WriteLine();
                    Console.WriteLine("Procesando archivo: " + Path.GetFileName(archivo));

                    int enviados = ProcesarArchivoXML(archivo);
                    totalMensajes += enviados;

                    Console.WriteLine("Pagos enviados desde archivo: " + enviados);
                }

                Console.WriteLine();
                Console.WriteLine("Proceso finalizado");
                Console.WriteLine("Total mensajes enviados a MSMQ: " + totalMensajes);

            }
            catch (Exception e)
            {
                Console.WriteLine("ERROR");
                Console.WriteLine($"Error: {e.Message}");
                Console.WriteLine($"Error: {e.StackTrace}");
            }

            Console.WriteLine();
            Console.WriteLine("Presione una tecla para salir");
            Console.ReadKey();
        }

        private static void ValidarCola()
        {
            if (!MessageQueue.Exists(ColaMSMQ))
            {
                throw new Exception("No existe la cola MSMQ: "+ColaMSMQ);

            }
        }

        private static int ProcesarArchivoXML (string rutaArchivo)
        {
            XDocument documento = XDocument.Load(rutaArchivo);
            string nombreArchivo = Path.GetFileNameWithoutExtension(rutaArchivo);
            string codigoSucursal = ObtenerCodigoSucursal(nombreArchivo);

            var pagos = documento
                .Descendants()
                .Where(x => string.Equals(x.Name.LocalName, "pago", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if(pagos.Count == 0)
            {
                Console.WriteLine("No se encontraron nodos <pago> en el archivo ");
                return 0;
            }

            int contador = 0;

            using (MessageQueue cola = new MessageQueue(ColaMSMQ))
            {
                cola.Formatter = new XmlMessageFormatter(new Type[] { typeof(string) });
                foreach (XElement pago in pagos)
                {
                    string mensajeXML = CrearMensajePagoIndividual(pago, codigoSucursal, rutaArchivo);
                    Message mensaje = new Message
                    {
                        Body = mensajeXML,
                        Label = "Pago XML " + codigoSucursal
                    };

                    cola.Send(mensaje, MessageQueueTransactionType.Single);
                    contador++;

                    Console.WriteLine("Mensaje enviado a MSMQ. Pago " + contador);
                }
            }

            return contador;
        }

        private static string ObtenerCodigoSucursal(string nombreArchivo)
        {
            string[] partes = nombreArchivo.Split('-');

            if (partes.Length > 0 && partes[0].StartsWith("suc_"))
            {
                return partes[0].Replace("suc_", "");
            }

            return "000";
        }

        private static string CrearMensajePagoIndividual(XElement pago, string codigoSucursal,string rutaArchivo)
        {
            XElement mensaje = new XElement("pagoSucursal",
                new XElement("codigoSucursal", codigoSucursal),
                new XElement("ArchivoOrigen", Path.GetFileName(rutaArchivo)),
                new XElement("fechaProceso", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")),
                new XElement("pago", pago.Elements())
            );

            return mensaje.ToString(SaveOptions.DisableFormatting);
        }
    }
}