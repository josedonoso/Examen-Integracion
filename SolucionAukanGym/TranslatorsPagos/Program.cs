using Newtonsoft.Json.Linq;
using System;
using System.Messaging;
using System.Xml.Linq;

namespace TranslatorsPagos
{
    internal class Program
    {
        private const string colaXML = @".\private$\jdo_suc_pagos";
        private const string colaWeb = @".\private$\jdo_web_pagos";
        private const string colaCanonica = @".\private$\jdo_pagos";

        static void Main(string[] args)
        {
            Console.WriteLine("=== TRANSLATORS PAGOS - AUKAN GYM === ");
            try
            {
                ValidarColas();

                int totalXml = ProcesarColaXml();
                int totalWeb = ProcesarColaWeb();

                Console.WriteLine();
                Console.WriteLine("Proceso finalizado correctamente. ");
                Console.WriteLine("Mensajes XML traducidos: " + totalXml);
                Console.WriteLine("Mensajes Web traducidos: " + totalWeb);
                Console.WriteLine("Total envoados a cola canonica: " + (totalXml + totalWeb));

            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR: ");
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
            }

            Console.WriteLine();
            Console.WriteLine("Presione una tecla para salir.");
            Console.ReadKey();
        }

        private static void ValidarColas()
        {
            if (!MessageQueue.Exists(colaXML))
            {
                throw new Exception("No existe la cola MSMQ: " + colaXML);
            }
            if (!MessageQueue.Exists(colaWeb))
            {
                throw new Exception("No existe la cola MSMQ: " + colaWeb);
            }
            if (!MessageQueue.Exists(colaCanonica))
            {
                throw new Exception("No existe la cola MSMQ: " + colaCanonica);
            }
        }

        private static int ProcesarColaXml()

        {

            Console.WriteLine();
            Console.WriteLine("Procesando cola XML: " + colaXML);

            int contador = 0;

            using (MessageQueue origen = new MessageQueue(colaXML))
            using (MessageQueue destino = new MessageQueue(colaCanonica))
            {

                origen.Formatter = new XmlMessageFormatter(new Type[] { typeof(string) });
                destino.Formatter = new XmlMessageFormatter(new Type[] { typeof(string) });

                while (true)
                {
                    Message mensaje;
                    try
                    {
                        mensaje = origen.Receive(TimeSpan.FromSeconds(2), MessageQueueTransactionType.Single);
                    }
                    catch (MessageQueueException ex)
                    {
                        if (ex.MessageQueueErrorCode == MessageQueueErrorCode.IOTimeout)
                            break;
                        throw;
                    }

                    string xmlOriginal = mensaje.Body.ToString();
                    string jsonCanonico = TraducirXmlACanonico(xmlOriginal);

                    EnviarMensajes(destino, jsonCanonico, "Pago Canonico XML");

                    contador++;

                    Console.WriteLine("XML traducido y enviado a jdo_pagos: " + contador);
                }
            }
            return contador;
        }
        private static int ProcesarColaWeb()
        {
            Console.WriteLine();
            Console.WriteLine("Procesando cola web: " + colaWeb);

            int contador = 0;

            using (MessageQueue origen = new MessageQueue(colaWeb))
            using (MessageQueue destino = new MessageQueue(colaCanonica))
            {
                origen.Formatter = new XmlMessageFormatter(new Type[] { typeof(string) });
                destino.Formatter = new XmlMessageFormatter(new Type[] { typeof(string) });

                while (true)
                {
                    Message mensaje;
                    try
                    {
                        mensaje = origen.Receive(TimeSpan.FromSeconds(2), MessageQueueTransactionType.Single);
                    }
                    catch (MessageQueueException ex)
                    {
                        if (ex.MessageQueueErrorCode == MessageQueueErrorCode.IOTimeout)

                            break;
                        throw;

                    }

                    string jsonOriginal = mensaje.Body.ToString();
                    string jsonCanonico = TraducirWebACanonico(jsonOriginal);

                    EnviarMensajes(destino, jsonCanonico, "Pago Canonico Web");

                    contador++;
                    Console.WriteLine("Web traducido y enviado a jdo_pagos: " + contador);
                }
            }
            return contador;
        }

        private static void EnviarMensajes(MessageQueue cola, string cuerpo, string etiqueta)
        {
            Message mensaje = new Message
            {
                Body = cuerpo,
                Label = etiqueta
            };

            cola.Send(mensaje, MessageQueueTransactionType.Single);
        }

        private static string TraducirWebACanonico(string jsonOriginal)
        {
            JObject origen = JObject.Parse(jsonOriginal);

            string paymentType = Valor(origen, "paymentType");
            string medioPago = NormalizarMedioPago(paymentType);

            JObject canonico = new JObject
            {
                ["rutCliente"] = Valor(origen, "identifier"),
                ["monto"] = ValorNumero(origen, "amount"),
                ["medioPago"] = medioPago,
                ["origen"] = "WEB",
                ["codigoSucursal"] = null,
                ["codigoAutorizacion"] = Valor(origen, "authorizationCode"),
                ["tarjeta"] = Valor(origen, "card"),
                ["fechaProceso"] = DateTime.Now.ToString("yyyy - MM - ddTHH:mm: ss"),
                ["estado"] = "REGISTRADO"
            };

            return canonico.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string TraducirXmlACanonico(string xmlOriginal)
        {
            XDocument doc = XDocument.Parse(xmlOriginal);

            XElement root = doc.Root;

            string codigoSucursal = ObtenerValorXml(root, "codigoSucursal");

            XElement pago = buscarElemento(root, "pago");

            if (pago == null)
                throw new Exception("No se encontro nodo pago dentro del mensaje xml");

            string rut = ObtenerPrimerValorDisponible(pago, "rut", "rutCliente", "identifier", "cliente", "clienteId");
            string montoTexto = ObtenerPrimerValorDisponible(pago, "monto", "amount", "valor");
            string formaPago = ObtenerPrimerValorDisponible(pago, "formaPago", "paymentType", "tipoPago", "medioPago");
            string autorizacion = ObtenerPrimerValorDisponible(pago, "codigoAutorizacion", "authorizationCode", "autorizacion");
            string tarjeta = ObtenerPrimerValorDisponible(pago, "tarjeta", "card");

            decimal monto = 0;
            decimal.TryParse(montoTexto, out monto);
            JObject canonico = new JObject
            {
                ["rutCliente"] = rut,
                ["monto"] = monto,
                ["medioPago"] = NormalizarMedioPago(formaPago),
                ["origen"] = "SUCURSAL",
                ["codigoSucursal"] = codigoSucursal,
                ["codigoAutorizacion"] = autorizacion,
                ["tarjeta"] = tarjeta,
                ["fechaProceso"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                ["estado"] = "REGISTRADO"
            };

            return canonico.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string NormalizarMedioPago(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
                return "NO_INFORMADO";

            valor = valor.Trim().ToUpper();

            if (valor == "CREDIT CARD" || valor == "TC" || valor.Contains("CREDIT"))
                return "TC";
            if (valor == "DEBIT CARD" || valor == "TD" || valor.Contains("DEBIT"))
                return "TD";
            if (valor == "EF" || valor.Contains("EFECTIVO") || valor.Contains("CASH"))
                return "EF";

            return valor;
        }

        private static string Valor(JObject obj, string nombre)
        {
            return obj[nombre] == null ? "" : obj[nombre].ToString();
        }

        private static JToken ValorNumero(JObject obj, string nombre)
        {
            if (obj[nombre] == null)
                return 0;

            return obj[nombre];
        }

        private static XElement buscarElemento(XElement root, string nombre)
        {
            foreach (XElement elemento in root.DescendantsAndSelf())
            {
                if (string.Equals(elemento.Name.LocalName, nombre, StringComparison.OrdinalIgnoreCase))
                    return elemento;
            }

            return null;
        }

        private static string ObtenerValorXml(XElement root, string nombre)
        {
            XElement elemento = buscarElemento(root, nombre);
            return elemento == null ? "" : elemento.Value.Trim();
        }

        private static string ObtenerPrimerValorDisponible(XElement root, params string[] nombres)
        {
            foreach (string nombre in nombres)
            {
                string valor = ObtenerValorXml(root, nombre);
                if (!string.IsNullOrWhiteSpace(valor))
                    return valor;
            }
            return "";
        }
    }
}
