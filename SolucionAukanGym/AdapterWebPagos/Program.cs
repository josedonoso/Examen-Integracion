using Newtonsoft.Json.Linq;
using System;
using System.Messaging;
using System.Net.Http;
using System.Threading.Tasks;

namespace AdapterWebPagos
{
    internal class Program
    {
        private const string apiUrl = "http://localhost:5000/api/payments/today";
        private const string colaMSMQ = @".\private$\jdo_web_pagos";

        static void Main(string[] args)
        {
            Console.WriteLine("=== Adapter Web pagos - Aukan Gym ===");

            try
            {
                Ejecutar().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR: ");
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
            }

            Console.WriteLine();
            Console.WriteLine("Presione una tecla para salir ,...");
            Console.ReadKey();
        }

        private static async Task Ejecutar()
        {
            ValidarCola();

            Console.WriteLine("API origen: " + apiUrl);
            Console.WriteLine("Cola MSMQ destino: " + colaMSMQ);

            string json = await ConsultarPagosWeb();
            if (string.IsNullOrWhiteSpace(json))
            {
                Console.WriteLine("La api no devolvio datos ");
                return;
            }

            JArray pagos = JArray.Parse(json);

            Console.WriteLine("Pagos recibidos desde API: " + pagos.Count);
            int totalEnviados = 0;
            using (MessageQueue cola = new MessageQueue(colaMSMQ))
            {
                cola.Formatter = new XmlMessageFormatter(new Type[] { typeof(string) });
                foreach (JToken pago in pagos)
                {
                    string pagoJson = pago.ToString(Newtonsoft.Json.Formatting.None);
                    Message mensaje = new Message
                    {
                        Body = pagoJson,
                        Label = "Pago Web REST"
                    };

                    cola.Send(mensaje, MessageQueueTransactionType.Single);
                    totalEnviados++;
                    Console.WriteLine("Mensaje enviado a MSMQ. Pago web " + totalEnviados);

                }
            }

            Console.WriteLine();
            Console.WriteLine("Proceso finalizado correctaente.");
            Console.WriteLine("Total mensajes enviado a MSMQ: " + totalEnviados);

        }

        private static void ValidarCola()
        {
            if (!MessageQueue.Exists(colaMSMQ))
            {
                throw new Exception("No existe la cola MSMQ: " + colaMSMQ);
            }
        }

        private static async Task<string> ConsultarPagosWeb()
        {
            using (HttpClient cliente = new HttpClient())
            {
                HttpResponseMessage respuesta = await cliente.GetAsync(apiUrl);
                if (!respuesta.IsSuccessStatusCode)
                {
                    throw new Exception("Error consultando API WebPagos. Codigo HTTP: " + respuesta.StatusCode);

                }

                return await respuesta.Content.ReadAsStringAsync();
            }
        }
    }
}
