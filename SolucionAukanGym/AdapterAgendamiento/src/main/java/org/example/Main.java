package org.example;

import org.apache.activemq.artemis.jms.client.ActiveMQConnectionFactory;

import org.json.JSONObject;

import javax.jms.Connection;

import javax.jms.ConnectionFactory;
import javax.jms.MessageConsumer;
import javax.jms.Queue;
import javax.jms.Session;
import javax.jms.TextMessage;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.io.InputStream;

public class Main {

    private static final String ARTEMIS_URL = "tcp://10.0.0.5:61616";
    private static final String ARTEMIS_USER = "admin";
    private static final String ARTEMIS_PASSWORD = "admin";
    private static final String ARTEMIS_QUEUE = "jdo_amq_estado_cuenta";

    private static final String AGENDA_SERVICE_URL = "http://localhost:5002/AgendaService";

    public static void main(String[] args) {
        System.out.println("=== ADAPTER AGENDAMIENTO SOAP - Aukan GYM ===");
        System.out.println("Origen ActiveMQ: " + ARTEMIS_URL);
        System.out.println("Cola ActiveMQ: " + ARTEMIS_QUEUE);
        System.out.println("Destino SOAP: " + AGENDA_SERVICE_URL);

        int procesados = 0;

        try {
            ConnectionFactory factory = new ActiveMQConnectionFactory(ARTEMIS_URL);

            try (Connection connection = factory.createConnection(ARTEMIS_USER, ARTEMIS_PASSWORD)) {
                connection.start();

                Session session = connection.createSession(false, Session.AUTO_ACKNOWLEDGE);
                Queue queue = session.createQueue(ARTEMIS_QUEUE);
                MessageConsumer consumer = session.createConsumer(queue);

                while (true) {
                    javax.jms.Message mensaje = consumer.receive(2000);

                    if (mensaje == null) {
                        break;
                    }

                    if (!(mensaje instanceof TextMessage)) {
                        System.out.println("Mensaje ignorado porque no es TextMessage.");
                        continue;
                    }

                    String cuerpo = ((TextMessage) mensaje).getText();

                    System.out.println();
                    System.out.println("Mensaje recibido desde ActiveMQ:");
                    System.out.println(cuerpo);

                    procesarEstadoCuenta(cuerpo);

                    procesados++;
                    System.out.println("Estado aplicado en SistemaAgendamiento: " + procesados);
                }
                consumer.close();
                session.close();
            }
            System.out.println();
            System.out.println("Proceso finalizado correctamente.");
            System.out.println("Total estados procesados: " + procesados);
        } catch (Exception e) {
            System.out.println("ERRROR en Adapter Agendamiento");
            e.printStackTrace();
        }
        System.out.println("Presione ENTER para salir ...");
        try {
            System.in.read();
        } catch (Exception ignored) {
        }
    }

    private static void procesarEstadoCuenta(String jsonEstadoCuenta) throws Exception {
        JSONObject estado = new JSONObject(jsonEstadoCuenta);

        String rutCliente = estado.optString("rutCliente", "").trim();
        String estadoCuenta = estado.optString("estadoCuenta", "").trim().toUpperCase();

        if (rutCliente.isEmpty()) {
            throw new RuntimeException("El mensaje no contiene rutCliente");
        }

        boolean alDia = estadoCuenta.equals("AL DIA") || estadoCuenta.equals("AL_DIA");

        String metodo = alDia ? "HabilitarUsuario" : "DeshabilitarUsuario";
        String respuesta = llamarSoapAgenda(metodo, rutCliente);

        System.out.println("Rut: " + rutCliente);
        System.out.println("Estado cuenta: " + estadoCuenta);
        System.out.println("Metodo SOAP llamado: " + metodo);
        System.out.println("Respuesta SOAP: " + respuesta);
    }

    private static String llamarSoapAgenda(String metodo, String clienteId) throws Exception {
        String soapAction = "http://tempuri.org/IAgendaService/" + metodo;

        String body =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                        "<soapenv:Envelope xmlns:soapenv=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:tem=\"http://tempuri.org/\">" +
                        "  <soapenv:Header/>" +
                        "  <soapenv:Body>" +
                        "    <tem:" + metodo + ">"+
        "      <tem:clienteId>" + escapeXml(clienteId) + "</tem:clienteId>" +
                "    </tem:" + metodo + ">" +
                "  </soapenv:Body>" +
                "</soapenv:Envelope>";

        byte[] requestBody = body.getBytes(StandardCharsets.UTF_8);

        URL url = new URL(AGENDA_SERVICE_URL);
        HttpURLConnection conn = (HttpURLConnection) url.openConnection();

        conn.setRequestMethod("POST");
        conn.setRequestProperty("Content-Type", "text/xml; charset=utf-8");
        conn.setRequestProperty("SOAPAction", "\"" + soapAction + "\"");
        conn.setDoOutput(true);

        try (OutputStream os = conn.getOutputStream()) {
            os.write(requestBody);
        }

        int status = conn.getResponseCode();

        byte[] responseBytes;
        if (status >= 200 && status < 300) {
            responseBytes = leerBytes(conn.getInputStream());
        } else {
            InputStream errorStream = conn.getErrorStream();
            responseBytes = errorStream != null ? leerBytes(errorStream) : new byte[0];
        }

        String response = new String(responseBytes, StandardCharsets.UTF_8);
        conn.disconnect();

        if (status < 200 || status >= 300) {
            throw new RuntimeException("Error SOAP " + metodo + ". HTTP " + status + ". Respuesta: " + response);
        }

        return response;
    }

    private static String escapeXml(String value) {
        return value
                .replace("&", "&amp;")
                .replace("<", "&lt;")
                .replace(">", "&gt;")
                .replace("\"", "&quot;")
                .replace("'", "&apos;");
    }

    private static byte[] leerBytes(InputStream inputStream) throws Exception{
        java.io.ByteArrayOutputStream buffer = new java.io.ByteArrayOutputStream();

        byte[] data = new byte[1024];
        int nRead;

        while ((nRead = inputStream.read(data, 0, data.length)) != -1){
            buffer.write(data, 0, nRead);
        }
        return buffer.toByteArray();

    }
}
