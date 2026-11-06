/* JS compartilhado dos protótipos: tema claro/escuro, avisos e dados de exemplo do Contexto do banco. */
(function () {
  const raiz = document.documentElement;
  const guardado = (() => { try { return localStorage.getItem('menage-proto-tema'); } catch { return null; } })();
  raiz.dataset.tema = guardado || (matchMedia('(prefers-color-scheme: light)').matches ? 'claro' : 'escuro');

  const M = window.Menage = {};
  M.alternarTema = () => {
    raiz.dataset.tema = raiz.dataset.tema === 'claro' ? 'escuro' : 'claro';
    try { localStorage.setItem('menage-proto-tema', raiz.dataset.tema); } catch {}
    document.querySelectorAll('.tema-btn').forEach(b => b.textContent = raiz.dataset.tema === 'claro' ? '☾ Escuro' : '☀ Claro');
  };
  M.aviso = (titulo, texto, cor = 'var(--verde)') => {
    let c = document.getElementById('avisos');
    if (!c) { c = document.createElement('div'); c.id = 'avisos'; document.body.append(c); }
    const a = document.createElement('div'); a.className = 'aviso'; a.style.setProperty('--c', cor);
    a.innerHTML = `<b>${titulo}</b><span class="fraco">${texto || ''}</span>`;
    c.append(a);
    setTimeout(() => { a.classList.add('sai'); setTimeout(() => a.remove(), 320); }, 2800);
  };
  M.esc = s => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
  M.icone = n => ({
    olho: '<path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12z"/><circle cx="12" cy="12" r="3"/>',
    olhoOff: '<path d="M3 3l18 18M10.6 5.1A10 10 0 0112 5c6.5 0 10 7 10 7a17 17 0 01-3.2 4M6.5 6.6C3.7 8.4 2 12 2 12s3.5 7 10 7a10 10 0 004-.8"/>',
    plus: '<path d="M12 5v14M5 12h14"/>', copia: '<rect x="9" y="9" width="11" height="11" rx="2"/><path d="M5 15V6a2 2 0 012-2h9"/>',
    lixo: '<path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3"/>', banco: '<ellipse cx="12" cy="5" rx="8" ry="3"/><path d="M4 5v14c0 1.7 3.6 3 8 3s8-1.3 8-3V5M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3"/>',
    ok: '<path d="M5 13l4 4L19 7"/>', x: '<path d="M6 6l12 12M18 6L6 18"/>', raio: '<path d="M13 2L4 14h7l-1 8 9-12h-7z"/>',
    pasta: '<path d="M3 7a2 2 0 012-2h4l2 2h8a2 2 0 012 2v8a2 2 0 01-2 2H5a2 2 0 01-2-2z"/>', salvar: '<path d="M5 3h11l3 3v15H5zM8 3v6h7V3M8 21v-7h8v7"/>',
    seta: '<path d="M5 12h14M13 6l6 6-6 6"/>', volta: '<path d="M19 12H5M11 6l-6 6 6 6"/>', busca: '<circle cx="11" cy="11" r="7"/><path d="M21 21l-5-5"/>',
    servidor: '<rect x="3" y="4" width="18" height="7" rx="2"/><rect x="3" y="13" width="18" height="7" rx="2"/><path d="M7 7.5h.01M7 16.5h.01"/>',
    chave: '<circle cx="8" cy="15" r="4"/><path d="M11 12l9-9M16 7l3 3"/>', link: '<path d="M10 14a4 4 0 005.7 0l3-3a4 4 0 00-5.7-5.7l-1 1M14 10a4 4 0 00-5.7 0l-3 3a4 4 0 005.7 5.7l1-1"/>',
  }[n] || '');
  M.svg = (n, cls = '') => `<svg class="ico ${cls}" viewBox="0 0 24 24">${M.icone(n)}</svg>`;

  /* Dados de exemplo (realistas) */
  M.perfis = () => [
    { id: 1, nome: 'Local (desenvolvimento)', tipo: 'Oracle', host: 'localhost', porta: 1521, web: { usuario: 'startquimica', servico: 'wmw', senha: 'startq123' }, app: { usuario: 'startquimica', senha: 'startq123' }, int: { igual: true, usuario: '', senha: '' }, estado: 'ok' },
    { id: 2, nome: 'Homologação Vendas', tipo: 'Oracle', host: '10.0.2.45', porta: 1521, web: { usuario: 'wmwhomolog', servico: 'wmwhml', senha: 'h0m0l0g' }, app: { usuario: 'wmwhomolog_app', senha: 'h0m0l0g' }, int: { igual: false, usuario: 'wmwint', senha: 'int2024' }, estado: 'ok' },
    { id: 3, nome: 'Cliente Start Química', tipo: 'PostgreSQL', host: 'db.startquimica.local', porta: 5432, web: { usuario: 'sq_web', servico: 'startquimica', senha: 'sqweb' }, app: { usuario: 'sq_app', senha: 'sqapp' }, int: { igual: true, usuario: '', senha: '' }, estado: 'erro' },
    { id: 4, nome: 'Backup noturno (leitura)', tipo: 'SQL Server', host: 'srv-bkp-02', porta: 1433, web: { usuario: 'leitura', servico: 'WMW_BKP', senha: 'leit' }, app: { usuario: 'leitura', senha: 'leit' }, int: { igual: true, usuario: '', senha: '' }, estado: 'aviso' },
  ];
  M.portaPadrao = { Oracle: 1521, PostgreSQL: 5432, 'SQL Server': 1433 };
  M.url = (p, quem) => {
    const prog = `${quem}_${p.web.usuario || 'usuario'}`;
    if (p.tipo === 'Oracle') return `jdbc:oracle:thin:@${p.host}:${p.porta}/${p.web.servico}?v$session.program=${prog}`;
    if (p.tipo === 'PostgreSQL') return `jdbc:postgresql://${p.host}:${p.porta}/${p.web.servico}?ApplicationName=${prog}`;
    return `jdbc:sqlserver://${p.host}:${p.porta};databaseName=${p.web.servico};applicationName=${prog}`;
  };
  const drivers = { Oracle: ['oracle.jdbc.OracleDriver', 'select 1 from dual'], PostgreSQL: ['org.postgresql.Driver', 'select 1'], 'SQL Server': ['com.microsoft.sqlserver.jdbc.SQLServerDriver', 'select 1'] };
  /* Gera o XML com realce; cada linha tem data-k para destacar a que mudou. */
  M.xml = (p, anterior) => {
    const [drv, val] = drivers[p.tipo];
    const rec = (nome, quem, usuario) => {
      const u = M.url(p, quem), l = [];
      const at = (k, v, extra = '') => `  <span class="a">${k}</span>=<span class="v" ${extra}>"${M.esc(v)}"</span>`;
      l.push(`<span class="t">&lt;Resource</span> <span class="a">name</span>=<span class="v">"jdbc/${nome}"</span> <span class="a">url</span>=<span class="v" data-k="${nome}u">"${M.esc(u)}"</span>`);
      l.push(at('username', usuario, `data-k="${nome}n"`) + ' <span class="a">password</span>=<span class="v">"***"</span>');
      l.push(at('validationQuery', val) + ' <span class="a">driverClassName</span>=<span class="v">"' + drv + '"</span>');
      l.push('  <span class="a">maxTotal</span>=<span class="v">"200"</span> <span class="a">maxIdle</span>=<span class="v">"20"</span> <span class="a">minIdle</span>=<span class="v">"3"</span> <span class="a">maxWaitMillis</span>=<span class="v">"30000"</span>');
      l.push('  <span class="a">auth</span>=<span class="v">"Container"</span> <span class="a">type</span>=<span class="v">"javax.sql.DataSource"</span>');
      l.push('<span class="t">/&gt;</span>');
      return l.join('\n');
    };
    const intU = p.int.igual ? p.web.usuario : p.int.usuario;
    const html = [rec('LAVENDEREWEBDS', 'web', p.web.usuario), rec('LAVENDEREPDADS', 'app', p.app.usuario), rec('LAVENDEREINTDS', 'int', intU)].join('\n\n');
    return html;
  };
  /* Teste de conexão simulado: falha para o perfil com estado 'erro' ou senha vazia */
  M.testar = (p, quem) => new Promise(res => setTimeout(() => {
    const c = (quem === 'int' && p.int.igual) ? p.web : (p[quem] || p.web), ok = p.estado !== 'erro' && p.host && c.senha !== '' ;
    res({ ok, ms: 60 + Math.round(Math.random() * 240), msg: ok ? 'Conectado' : 'ORA-12541: sem listener em ' + p.host + ':' + p.porta });
  }, 700 + Math.random() * 700));
  M.novoPerfil = n => ({ id: Date.now(), nome: 'Novo perfil ' + n, tipo: 'Oracle', host: 'localhost', porta: 1521, web: { usuario: '', servico: 'wmw', senha: '' }, app: { usuario: '', senha: '' }, int: { igual: true, usuario: '', senha: '' }, estado: 'aviso' });

  document.addEventListener('DOMContentLoaded', () => {
    document.querySelectorAll('.tema-btn').forEach(b => { b.textContent = raiz.dataset.tema === 'claro' ? '☾ Escuro' : '☀ Claro'; b.onclick = M.alternarTema; });
  });
})();
