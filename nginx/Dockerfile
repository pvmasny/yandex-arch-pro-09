# nginx/Dockerfile
FROM nginx:alpine

# Установка инструментов для отладки
RUN apk add --no-cache curl vim

# Создание директории для кеша
RUN mkdir -p /var/cache/nginx && \
    chown -R nginx:nginx /var/cache/nginx

# Копирование конфигурации
COPY nginx.conf /etc/nginx/nginx.conf

# Проверка конфигурации
RUN nginx -t

EXPOSE 8082

CMD ["nginx", "-g", "daemon off;"]