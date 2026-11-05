import { m } from 'virtual:runic-translations/dashboard';
import { configureLocaleResolver, createLocaleSource } from 'virtual:runic-translations/dashboard/runtime';

// The page's locale: the browser's language, resolved to a catalog locale (en or de), and a switch.
const locale = createLocaleSource({ initialLocale: navigator.language });
configureLocaleResolver(locale.getLocale);

function render() {
  document.documentElement.lang = locale.getLocale();
  document.querySelector('#title').textContent = m.page_title();
  document.querySelector('#summary').textContent = m.page_summary({ count: 3 });
  document.querySelector('#language').textContent = locale.getLocale() === 'de' ? 'English' : 'Deutsch';
}

document.querySelector('#language').addEventListener('click', () => {
  locale.setLocale(locale.getLocale() === 'de' ? 'en' : 'de');
  render();
});
render();
